using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages;

/// <summary>The immutable content of a published version.</summary>
public sealed record PageSnapshot(
    string Name, string Slug, string? MetaTitle, string? MetaDescription, string? OgImageUrl, bool NoIndex, bool ExperimentEnabled,
    Guid ExperimentId, JsonElement Variants);

/// <summary>A published page with the version that is live and its snapshot.</summary>
public sealed record LivePage(LandingPage Page, LandingPageVersion Version, PageSnapshot Snapshot);

/// <summary>Landing-page content rules shared by the staff API and the public renderer.</summary>
public sealed partial class LandingPageService(AppDbContext db, ImageUrlPolicy images)
{
    public static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    /// <summary>Parses and validates draft variants for a page of <paramref name="clientAccountId"/>.</summary>
    public async Task<IReadOnlyList<LandingVariant>> ParseVariantsAsync(Guid clientAccountId, JsonElement variants, CancellationToken ct)
    {
        var usableForms = await db.Set<Form>().AsNoTracking()
            .Where(f => f.ClientAccountId == clientAccountId && f.Status == FormStatus.Active).Select(f => f.Id).ToListAsync(ct);
        var context = new BlockValidationContext(url => images.IsAllowed(url), id => usableForms.Contains(id));
        try
        {
            return LandingBlocks.ParseVariants(variants, context);
        }
        catch (LandingValidationException ex)
        {
            throw new DomainException("landing.invalid_content", "The page content is invalid.", DomainErrorKind.Validation, ex.Errors);
        }
    }

    /// <summary>
    /// The published page answering <c>/lp/{client}/{slug}</c>. The address is part of the published snapshot: renaming the
    /// slug in the draft neither moves nor breaks the live page until the rename is published. Only pages whose draft slug
    /// matches, or whose draft differs from what is live, need their snapshot read.
    /// </summary>
    public async Task<LivePage?> FindLiveAsync(Guid clientAccountId, string slug, CancellationToken ct, Guid? exceptPageId = null)
    {
        var candidates = await db.Set<LandingPage>().AsNoTracking()
            .Where(p => p.ClientAccountId == clientAccountId && p.Status == LandingPageStatus.Published && p.PublishedVersionId != null &&
                        p.Id != exceptPageId && (p.Slug == slug || p.HasUnpublishedChanges))
            .ToListAsync(ct);
        foreach (var page in candidates.OrderByDescending(p => p.Slug == slug).ThenBy(p => p.Id))
        {
            var version = await db.Set<LandingPageVersion>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == page.PublishedVersionId, ct);
            if (version is null) continue;
            var snapshot = ReadSnapshot(version);
            if (snapshot.Slug == slug) return new LivePage(page, version, snapshot);
        }
        return null;
    }

    /// <summary>The live (published) slug of each page, for the pages among <paramref name="pages"/> that are published.</summary>
    public async Task<Dictionary<Guid, string>> LiveSlugsAsync(IEnumerable<LandingPage> pages, CancellationToken ct)
    {
        var published = pages.Where(p => p.Status == LandingPageStatus.Published && p.PublishedVersionId is not null).ToList();
        var result = published.Where(p => !p.HasUnpublishedChanges).ToDictionary(p => p.Id, p => p.Slug);
        var diverged = published.Where(p => p.HasUnpublishedChanges).Select(p => p.PublishedVersionId!.Value).ToList();
        if (diverged.Count == 0) return result;
        foreach (var version in await db.Set<LandingPageVersion>().AsNoTracking().Where(v => diverged.Contains(v.Id)).ToListAsync(ct))
            result[version.PageId] = ReadSnapshot(version).Slug;
        return result;
    }

    /// <summary>
    /// Whether <paramref name="slug"/> is taken for the client by another page, as its draft address or as its live one (a page
    /// whose rename is not published yet still holds its old address).
    /// </summary>
    public async Task<bool> SlugTakenAsync(Guid clientAccountId, string slug, Guid? exceptPageId, CancellationToken ct) =>
        await db.Set<LandingPage>().AnyAsync(p => p.ClientAccountId == clientAccountId && p.Slug == slug && p.Id != exceptPageId, ct)
        || await FindLiveAsync(clientAccountId, slug, ct, exceptPageId) is not null;

    public static IReadOnlyList<string> ValidateMeta(string? ogImageUrl, ImageUrlPolicy images)
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(ogImageUrl) && !images.IsAllowed(ogImageUrl)) errors.Add(ImageUrlPolicy.Message);
        return errors;
    }

    public static bool IsValidSlug(string slug) => SlugRegex().IsMatch(slug);

    public static string Slugify(string text)
    {
        var slug = NonSlugRegex().Replace(text.Trim().ToLowerInvariant(), "-").Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        if (slug.Length > 80) slug = slug[..80].Trim('-');
        return slug.Length == 0 ? "page" : slug;
    }

    /// <summary>Builds the snapshot JSON of the page's current draft.</summary>
    public static string BuildSnapshot(LandingPage page)
    {
        using var variants = JsonDocument.Parse(page.VariantsJson);
        var snapshot = new PageSnapshot(page.Name, page.Slug, page.MetaTitle, page.MetaDescription, page.OgImageUrl, page.NoIndex,
            page.ExperimentEnabled, page.ExperimentId, variants.RootElement.Clone());
        return JsonSerializer.Serialize(snapshot, SnapshotJson);
    }

    public static PageSnapshot ReadSnapshot(LandingPageVersion version)
    {
        if (Normalization.Sha256Hex(version.SnapshotJson) != version.ContentHash)
            throw new InvalidOperationException($"Landing page version {version.Id} failed its integrity check.");
        return JsonSerializer.Deserialize<PageSnapshot>(version.SnapshotJson, SnapshotJson)
               ?? throw new InvalidOperationException("Empty snapshot.");
    }

    /// <summary>Variants of a snapshot as (key, weight, blocks JSON array).</summary>
    public static List<(string Key, string Name, int Weight, JsonElement Blocks)> Variants(JsonElement variants) =>
        variants.EnumerateArray().Select(v => (
            v.GetProperty("key").GetString()!,
            v.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            v.TryGetProperty("weight", out var w) ? w.GetInt32() : 50,
            v.GetProperty("blocks").Clone())).ToList();

    public static IEnumerable<Guid> FormIdsIn(JsonElement variants) =>
        variants.EnumerateArray().SelectMany(v => v.GetProperty("blocks").EnumerateArray())
            .Where(b => b.GetProperty("type").GetString() == "form")
            .Select(b => b.GetProperty("props").TryGetProperty("formId", out var id) && id.TryGetGuid(out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct();

    /// <summary>Replaces template placeholders ({{form}}, {{in14days}}) in a template's block JSON and wraps it as variant A.</summary>
    public static JsonElement InstantiateTemplate(string blocksJson, Guid? formId, DateTime now)
    {
        var node = JsonNode.Parse(blocksJson)!.AsArray();
        foreach (var block in node.ToList())
        {
            var props = block!["props"]!.AsObject();
            if (props["formId"]?.GetValue<string>() == Templates.TemplateCatalog.FormPlaceholder)
            {
                if (formId is null) node.Remove(block);
                else props["formId"] = formId.Value.ToString();
            }
            if (props["endsAt"]?.GetValue<string>() == Templates.TemplateCatalog.CountdownPlaceholder)
                props["endsAt"] = now.Date.AddDays(14).AddHours(15).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }
        var variants = new JsonArray(new JsonObject { ["key"] = "A", ["name"] = "Control", ["weight"] = 50, ["blocks"] = node });
        return JsonDocument.Parse(variants.ToJsonString()).RootElement.Clone();
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugRegex();
}
