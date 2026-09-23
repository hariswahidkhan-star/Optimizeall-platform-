using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Delivery;

/// <summary>A message rendered for one contact.</summary>
public sealed record ComposedEmail(string Subject, string Html, string Text, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Turns a design + contact into a personalized, tracked email: merge values (with the signed unsubscribe/preferences
/// URLs), click-tracked links (stored link ids only), the open pixel and RFC 8058 one-click unsubscribe headers.
/// </summary>
public sealed class MessageComposer(AppDbContext db, TrackingTokens tokens, EmailMarketingUrls urls, TimeProvider clock, IDatabaseDialect dialect)
{
    /// <summary>Merge values for a contact (custom fields as <c>custom.key</c>, event properties as <c>event.key</c>).</summary>
    public async Task<Dictionary<string, string?>> ValuesAsync(
        Subscriber s, EmailWorkspaceSettings settings, TokenSource source, Guid messageId, string? campaignName, string? eventJson, CancellationToken ct)
    {
        var fields = await db.Set<SubscriberField>().AsNoTracking().Where(f => f.SubscriberId == s.Id).ToListAsync(ct);
        var messageOrSubscriber = source == TokenSource.Subscriber ? s.Id : messageId;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["first_name"] = s.FirstName,
            ["last_name"] = s.LastName,
            ["full_name"] = string.Join(' ', new[] { s.FirstName, s.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))),
            ["email"] = s.Email,
            ["phone"] = s.Phone,
            ["country"] = s.CountryCode,
            ["language"] = s.Language,
            ["org_name"] = settings.OrganizationName,
            ["org_address"] = settings.PhysicalAddress,
            ["current_year"] = clock.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture),
            ["campaign_name"] = campaignName,
            ["unsubscribe_url"] = urls.Unsubscribe(tokens.Create(TokenPurpose.Unsubscribe, source, messageOrSubscriber)),
            ["preferences_url"] = urls.Preferences(tokens.Create(TokenPurpose.Preferences, source, messageOrSubscriber)),
        };
        foreach (var f in fields) values["custom." + f.Key] = f.Value;
        if (!string.IsNullOrWhiteSpace(eventJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(eventJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (ContactRules.IsValidFieldKey(p.Name))
                            values["event." + p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
            }
            catch (JsonException) { }
        }
        return values;
    }

    /// <summary>Sample values for previews and test sends (no real contact data, no working unsubscribe token).</summary>
    public Dictionary<string, string?> SampleValues(EmailWorkspaceSettings settings, string? campaignName = null) => new(StringComparer.Ordinal)
    {
        ["first_name"] = "Alex",
        ["last_name"] = "Morgan",
        ["full_name"] = "Alex Morgan",
        ["email"] = "alex@example.com",
        ["phone"] = "+15555550123",
        ["country"] = "US",
        ["language"] = "en",
        ["org_name"] = string.IsNullOrWhiteSpace(settings.OrganizationName) ? "Your organization" : settings.OrganizationName,
        ["org_address"] = string.IsNullOrWhiteSpace(settings.PhysicalAddress) ? "[Physical address missing — add it in Email settings]" : settings.PhysicalAddress,
        ["current_year"] = clock.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture),
        ["campaign_name"] = campaignName,
        ["unsubscribe_url"] = urls.AppBaseUrl + "/email/unsubscribe/preview",
        ["preferences_url"] = urls.AppBaseUrl + "/email/preferences/preview",
    };

    /// <summary>
    /// Renders a design for one message. When <paramref name="linkIds"/> is given, links become signed click URLs
    /// and an open pixel is added; otherwise links are only merged (previews/tests).
    /// </summary>
    public ComposedEmail Compose(EmailDesign design, string subject, string? preview, IReadOnlyDictionary<string, string?> values,
        TokenSource source, Guid messageId, IReadOnlyDictionary<string, Guid>? linkIds, string? feedbackId = null, string language = "en")
    {
        Func<string, string?>? rewriter = null;
        string? pixel = null;
        if (linkIds is not null)
        {
            rewriter = raw => linkIds.TryGetValue(raw, out var linkId) ? urls.Click(tokens.Create(TokenPurpose.Click, source, messageId, linkId)) : null;
            pixel = urls.OpenPixel(tokens.Create(TokenPurpose.Open, source, messageId));
        }
        var mergedSubject = MergeTags.Render(subject, values, MergeContext.Text).Replace('\n', ' ').Trim();
        var rendered = EmailRenderer.Render(design, new RenderOptions
        {
            Subject = mergedSubject,
            PreviewText = MergeTags.Render(preview, values, MergeContext.Text),
            Values = values,
            LinkRewriter = rewriter,
            OpenPixelUrl = pixel,
            Language = language,
        });
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("unsubscribe_url", out var unsubscribe) && unsubscribe is not null && linkIds is not null)
        {
            headers["List-Unsubscribe"] = $"<{unsubscribe}>";
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
        }
        if (feedbackId is not null) headers["Feedback-ID"] = feedbackId;
        return new ComposedEmail(mergedSubject, rendered.Html, rendered.Text, headers);
    }

    /// <summary>SMS text with merge tags resolved as plain text.</summary>
    public static string ComposeText(string body, IReadOnlyDictionary<string, string?> values) =>
        MergeTags.Render(body, values, MergeContext.Text).Trim();

    /// <summary>
    /// Stores every trackable link of the content under <paramref name="sourceKey"/> (idempotent; safe under concurrent
    /// workers through the unique (source, url hash) index) and returns url → link id.
    /// </summary>
    public async Task<Dictionary<string, Guid>> EnsureLinksAsync(Guid? clientAccountId, string sourceKey, IEnumerable<string> links, CancellationToken ct)
    {
        var wanted = links.Distinct().ToList();
        for (var attempt = 0; ; attempt++)
        {
            var existing = await db.Set<TrackedLink>().AsNoTracking().Where(l => l.SourceKey == sourceKey)
                .Select(l => new { l.Id, l.Url, l.Position }).ToListAsync(ct);
            var map = existing.GroupBy(l => l.Url).ToDictionary(g => g.Key, g => g.First().Id);
            var missing = wanted.Where(u => !map.ContainsKey(u)).ToList();
            if (missing.Count == 0) return map;
            var next = existing.Count == 0 ? 0 : existing.Max(l => l.Position) + 1;
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var url in missing)
                db.Set<TrackedLink>().Add(new TrackedLink
                {
                    ClientAccountId = clientAccountId, SourceKey = sourceKey, Url = url, UrlHash = Text.UrlHash(url), Position = next++, CreatedAt = now,
                });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex) && attempt < 3)
            {
                // Another worker stored the same links; reload.
            }
            finally
            {
                foreach (var entry in db.ChangeTracker.Entries<TrackedLink>().ToList()) entry.State = EntityState.Detached;
            }
        }
    }
}

/// <summary>Loads (creating on first use) a workspace's email settings, defaulting from the client account.</summary>
public sealed class EmailSettingsStore(AppDbContext db, IDatabaseDialect dialect)
{
    public async Task<EmailWorkspaceSettings> GetAsync(Guid? clientAccountId, CancellationToken ct)
    {
        var key = Workspace.Key(clientAccountId);
        var existing = await db.Set<EmailWorkspaceSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.ScopeKey == key, ct);
        if (existing is not null) return existing;

        var client = clientAccountId is null ? null : await db.Set<ClientAccount>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientAccountId, ct);
        var settings = new EmailWorkspaceSettings
        {
            ClientAccountId = clientAccountId,
            ScopeKey = key,
            OrganizationName = client?.Name ?? "Optimize All",
            PhysicalAddress = client?.BillingAddress ?? string.Empty,
            DefaultTimeZone = client?.TimeZone is { Length: > 0 } tz ? tz : "UTC",
            CostCurrency = "USD",
        };
        db.Set<EmailWorkspaceSettings>().Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(settings).State = EntityState.Detached;
            return await db.Set<EmailWorkspaceSettings>().AsNoTracking().FirstAsync(s => s.ScopeKey == key, ct);
        }
        db.Entry(settings).State = EntityState.Detached;
        return settings;
    }
}
