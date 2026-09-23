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
