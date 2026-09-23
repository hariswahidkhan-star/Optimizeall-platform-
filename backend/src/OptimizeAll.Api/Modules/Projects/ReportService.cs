using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Projects;

/// <summary>What a report section provider is asked to build.</summary>
public sealed record ClientReportContext(
    Guid ClientAccountId, string ClientName, string Currency, string TimeZone, DateOnly PeriodStart, DateOnly PeriodEnd, Guid? ProjectId);

/// <summary>Section content from a provider. Every KPI must carry its <see cref="ReportKpi.Source"/> and measurement label.</summary>
public sealed record ClientReportSectionResult(string? Summary, IReadOnlyList<ReportKpi> Kpis, string? Note = null);

/// <summary>
/// Contributes data to monthly client reports. Implemented by the delivery module (delivery, feedback) and by the
/// marketing modules (SEO, social, ads, email, content): register with
/// <c>services.AddScoped&lt;IClientReportSection, MySection&gt;()</c>. A report template section with
/// <c>ProviderKey == Key</c> is filled from it. Return null when there is no data for the client/period (the section
/// then stays manual). Providers must be read-only, honest about how each value was obtained
/// (<see cref="KpiMeasurement.Measured"/> for platform-reported data, <see cref="KpiMeasurement.Estimated"/> for modelled
/// values) and must never invent figures when an integration is not configured.
/// </summary>
public interface IClientReportSection
{
    /// <summary>Stable key referenced by report templates, e.g. "seo", "social", "ads", "email", "content".</summary>
    string Key { get; }

    string Title { get; }

    /// <summary>Service line slug (see project service lines).</summary>
    string ServiceLine { get; }

    Task<ClientReportSectionResult?> BuildAsync(ClientReportContext context, CancellationToken ct);
}

/// <summary>Built-in provider: delivery output for the month (from this platform's own records → Measured).</summary>
public sealed class DeliveryReportSection(AppDbContext db) : IClientReportSection
{
    public string Key => "delivery";
    public string Title => "Delivery this month";
    public string ServiceLine => "strategy";

    public async Task<ClientReportSectionResult?> BuildAsync(ClientReportContext c, CancellationToken ct)
    {
        var from = c.PeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = c.PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var approved = await db.Set<Deliverable>().AsNoTracking()
            .CountAsync(d => d.ClientAccountId == c.ClientAccountId && d.ApprovedAt >= from && d.ApprovedAt < to, ct);
        var tasksDone = await db.Set<ProjectTask>().AsNoTracking()
            .CountAsync(t => t.ClientAccountId == c.ClientAccountId && t.CompletedAt >= from && t.CompletedAt < to, ct);
        var minutes = await db.Set<TimeEntry>().AsNoTracking()
            .Where(e => e.ClientAccountId == c.ClientAccountId && e.Date >= c.PeriodStart && e.Date <= c.PeriodEnd && e.RunningUserId == null)
            .SumAsync(e => (int?)e.Minutes, ct) ?? 0;
        const string source = "Optimize All delivery records";
        return new ClientReportSectionResult(null, new List<ReportKpi>
        {
            new("deliverables_approved", "Deliverables approved", approved, null, null, source, KpiMeasurement.Measured),
            new("tasks_completed", "Tasks completed", tasksDone, null, null, source, KpiMeasurement.Measured),
            new("hours_delivered", "Hours delivered", BudgetMath.Hours(minutes), "h", null, source, KpiMeasurement.Measured),
        });
    }
}

/// <summary>Built-in provider: client satisfaction (CSAT this month, latest NPS) from in-portal surveys.</summary>
public sealed class FeedbackReportSection(AppDbContext db) : IClientReportSection
{
    public string Key => "feedback";
    public string Title => "Client satisfaction";
    public string ServiceLine => "strategy";

    public async Task<ClientReportSectionResult?> BuildAsync(ClientReportContext c, CancellationToken ct)
    {
        var from = c.PeriodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = c.PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var csat = await db.Set<ClientFeedback>().AsNoTracking()
            .Where(f => f.ClientAccountId == c.ClientAccountId && f.Kind == ClientFeedbackKind.Csat && f.CreatedAt >= from && f.CreatedAt < to)
            .Select(f => f.Score).ToListAsync(ct);
        var nps = await db.Set<ClientFeedback>().AsNoTracking()
            .Where(f => f.ClientAccountId == c.ClientAccountId && f.Kind == ClientFeedbackKind.Nps && f.CreatedAt < to)
            .OrderByDescending(f => f.CreatedAt).Select(f => (int?)f.Score).FirstOrDefaultAsync(ct);
        if (csat.Count == 0 && nps is null) return null;
        const string source = "Client portal surveys";
        var kpis = new List<ReportKpi>();
        if (csat.Count > 0)
            kpis.Add(new("csat", "Average CSAT", Math.Round((decimal)csat.Average(), 2), "/5", null, source, KpiMeasurement.Measured,
                $"{csat.Count} response{(csat.Count == 1 ? "" : "s")}"));
        if (nps is { } n) kpis.Add(new("nps_latest", "Latest NPS response", n, "/10", null, source, KpiMeasurement.Measured));
        return new ClientReportSectionResult(null, kpis);
    }
}

public sealed class ReportService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IDatabaseDialect dialect,
    IEnumerable<IClientReportSection> providers,
    TimeProvider clock,
    ILogger<ReportService> logger)
{
    public const string DefaultTemplateKey = "monthly-performance";
    private static readonly string[] Kinds = { "summary", "kpis", "channel", "wins", "plan", "custom" };
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<ReportProviderDto> Providers => providers.Select(p => new ReportProviderDto(p.Key, p.Title, p.ServiceLine)).ToList();

    private async Task<ClientReport> LoadAsync(Guid id, CancellationToken ct, bool tracked = true)
    {
        var q = tracked ? db.Set<ClientReport>() : db.Set<ClientReport>().AsNoTracking();
        return await (await scope.ApplyAsync(q, r => r.ClientAccountId, ct)).FirstOrDefaultAsync(r => r.Id == id, ct)
               ?? throw DomainException.NotFound("Report");
    }

    public async Task<PagedResult<ReportSummaryDto>> ListAsync(ReportListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<ClientReport>().AsNoTracking(), r => r.ClientAccountId, ct);
        if (query.ClientId is { } cid) q = q.Where(r => r.ClientAccountId == cid);
        if (query.Status is { } s) q = q.Where(r => r.Status == s);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.PeriodStart).ThenBy(r => r.Title).Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<ReportSummaryDto>(await SummariesAsync(rows, ct), total, query.Page, query.PageSize);
    }

    private async Task<List<ReportSummaryDto>> SummariesAsync(List<ClientReport> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.ClientAccountId).Distinct().ToList();
        var names = await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(r => new ReportSummaryDto(r.Id, r.ClientAccountId, names.GetValueOrDefault(r.ClientAccountId) ?? "", r.Title,
            r.PeriodStart, r.PeriodEnd, r.Status, r.PublishedAt, r.AutoKey != null, r.UpdatedAt)).ToList();
    }

    public async Task<ReportDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadAsync(id, ct, tracked: false), ct);

    private async Task<ReportDto> ToDtoAsync(ClientReport r, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == r.ClientAccountId).Select(c => c.Name).FirstAsync(ct);
        var publisher = r.PublishedByUserId is { } p
            ? await db.Set<User>().AsNoTracking().Where(u => u.Id == p).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;
        return new ReportDto(r.Id, r.ClientAccountId, client, r.ProjectId, r.Title, r.PeriodStart, r.PeriodEnd, r.Status, r.TemplateKey,
            r.Sections, r.PublishedAt, publisher, Providers, r.UpdatedAt, r.ConcurrencyStamp);
    }

    public static DateOnly ParsePeriod(string period) =>
        DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<ReportDto> CreateAsync(CreateReportRequest r, CancellationToken ct)
    {
        var clientId = r.ClientId!.Value;
        await scope.EnsureAccessAsync(clientId, ct: ct);
        if (r.ProjectId is { } pid && !await db.Set<Project>().AnyAsync(p => p.Id == pid && p.ClientAccountId == clientId, ct))
            throw DeliveryRules.Invalid("report.invalid_project", "projectId", "That project belongs to another client.");
        var report = await BuildDraftAsync(clientId, r.ProjectId, ParsePeriod(r.Period), r.TemplateKey ?? DefaultTemplateKey, r.Title, currentUser.Id, null, ct);
        db.Set<ClientReport>().Add(report);
        audit.Record("report.created", nameof(ClientReport), report.Id, after: new { report.ClientAccountId, report.PeriodStart, report.TemplateKey });
        await db.SaveChangesAsync(ct);
        return await GetAsync(report.Id, ct);
    }

    /// <summary>Builds (does not save) a draft from a template, filling provider sections.</summary>
    internal async Task<ClientReport> BuildDraftAsync(Guid clientId, Guid? projectId, DateOnly periodStart, string templateKey, string? title,
        Guid? createdBy, string? autoKey, CancellationToken ct)
    {
        var template = await db.Set<ReportTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Key == templateKey, ct)
                       ?? throw DeliveryRules.Invalid("report.invalid_template", "templateKey", "That report template doesn't exist.");
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var context = new ClientReportContext(clientId, client.Name, client.Currency, client.TimeZone, periodStart, periodEnd, projectId);
        var sections = new List<ReportSection>();
        foreach (var s in template.Sections)
            sections.Add(await BuildSectionAsync(s.Key, s.Kind, s.Title, s.ProviderKey, s.Prompt, context, ct));
        return new ClientReport
        {
            ClientAccountId = clientId, ProjectId = projectId, PeriodStart = periodStart, PeriodEnd = periodEnd, TemplateKey = template.Key,
            Title = string.IsNullOrWhiteSpace(title) ? $"{client.Name} — {periodStart:MMMM yyyy} performance report" : title.Trim(),
            Sections = sections, CreatedByUserId = createdBy, AutoKey = autoKey,
        };
    }

    private async Task<ReportSection> BuildSectionAsync(string key, string kind, string title, string? providerKey, string? prompt,
        ClientReportContext context, CancellationToken ct)
    {
        if (providerKey is null) return new ReportSection(key, kind, title, prompt is null ? null : $"<!-- {prompt} -->", new List<ReportKpi>());
        var provider = providers.FirstOrDefault(p => p.Key == providerKey);
        if (provider is null)
            return new ReportSection(key, kind, title, null, new List<ReportKpi>(), providerKey,
                "No data source is connected for this section yet. Enter figures manually (they will be labelled Manual).");
        try
        {
            var result = await provider.BuildAsync(context, ct);
            if (result is null)
                return new ReportSection(key, kind, title, null, new List<ReportKpi>(), providerKey, "No data for this period.");
            var kpis = result.Kpis.Where(k => !string.IsNullOrWhiteSpace(k.Source)).ToList();
            return new ReportSection(key, kind, title, result.Summary, kpis, providerKey, result.Note);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Report section provider {Provider} failed", providerKey);
            return new ReportSection(key, kind, title, null, new List<ReportKpi>(), providerKey, "The data source failed; try refreshing the section.");
        }
    }

    public async Task<ReportDto> UpdateAsync(Guid id, UpdateReportRequest r, CancellationToken ct)
    {
        var report = await LoadAsync(id, ct);
        DeliveryRules.EnsureStamp(report, r.ConcurrencyStamp, db);
        if (report.Status == ReportStatus.Published)
            throw DomainException.Conflict("report.published", "Published reports can't be edited.");
        report.Title = r.Title.Trim();
        report.Sections = ValidateSections(r.Sections);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public static List<ReportSection> ValidateSections(List<ReportSection> sections)
    {
        if (sections.Count > 30) throw DeliveryRules.Invalid("report.too_many_sections", "sections", "At most 30 sections.");
        var keys = new HashSet<string>();
        var result = new List<ReportSection>();
        foreach (var s in sections)
        {
            if (string.IsNullOrWhiteSpace(s.Key) || s.Key.Length > 64 || !keys.Add(s.Key))
                throw DeliveryRules.Invalid("report.invalid_section", "sections", "Each section needs a unique key.");
            if (!Kinds.Contains(s.Kind)) throw DeliveryRules.Invalid("report.invalid_section_kind", "sections", $"Unknown section kind '{s.Kind}'.");
            if (string.IsNullOrWhiteSpace(s.Title) || s.Title.Length > 200) throw DeliveryRules.Invalid("report.invalid_section_title", "sections", "Each section needs a title.");
            if (s.Body is { Length: > 20000 }) throw DeliveryRules.Invalid("report.section_too_long", "sections", "Sections are limited to 20,000 characters.");
            var kpis = s.Kpis ?? new List<ReportKpi>();
            if (kpis.Count > 40) throw DeliveryRules.Invalid("report.too_many_kpis", "sections", "At most 40 KPIs per section.");
            foreach (var k in kpis)
            {
                if (string.IsNullOrWhiteSpace(k.Label) || k.Label.Length > 120)
                    throw DeliveryRules.Invalid("report.invalid_kpi", "sections", "Each KPI needs a label.");
                if (string.IsNullOrWhiteSpace(k.Source) || k.Source.Length > 120)
                    throw DeliveryRules.Invalid("report.kpi_source_required", "sections", $"Say where “{k.Label}” comes from (its source).");
                if (!Enum.IsDefined(k.Measurement)) throw DeliveryRules.Invalid("report.invalid_measurement", "sections", "Unknown measurement label.");
            }
            result.Add(s with { Title = s.Title.Trim(), Kpis = kpis.Select(k => k with { Key = string.IsNullOrWhiteSpace(k.Key) ? Slug(k.Label) : k.Key }).ToList() });
        }
        return result;
    }

    private static string Slug(string label) => new string(label.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    /// <summary>Re-runs a section's provider (keeps manual KPIs typed into it).</summary>
    public async Task<ReportDto> RefreshSectionAsync(Guid id, RefreshSectionRequest r, CancellationToken ct)
    {
        var report = await LoadAsync(id, ct);
        DeliveryRules.EnsureStamp(report, r.ConcurrencyStamp, db);
        if (report.Status == ReportStatus.Published) throw DomainException.Conflict("report.published", "Published reports can't be edited.");
        var index = report.Sections.FindIndex(s => s.Key == r.SectionKey);
        if (index < 0) throw DomainException.NotFound("ReportSection");
        var section = report.Sections[index];
        if (section.ProviderKey is null) throw DomainException.Conflict("report.manual_section", "This section has no data source.");
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == report.ClientAccountId, ct);
        var context = new ClientReportContext(client.Id, client.Name, client.Currency, client.TimeZone, report.PeriodStart, report.PeriodEnd, report.ProjectId);
        var fresh = await BuildSectionAsync(section.Key, section.Kind, section.Title, section.ProviderKey, null, context, ct);
        var manual = section.Kpis.Where(k => k.Measurement == KpiMeasurement.Manual && fresh.Kpis.All(f => f.Key != k.Key));
        var sections = report.Sections.ToList();
        sections[index] = fresh with { Body = section.Body ?? fresh.Body, Kpis = fresh.Kpis.Concat(manual).ToList() };
        report.Sections = sections;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Publishes to the client portal and notifies the client's users (once, even if retried).</summary>
    public async Task<ReportDto> PublishAsync(Guid id, PublishReportRequest r, CancellationToken ct)
    {
        var report = await LoadAsync(id, ct);
        DeliveryRules.EnsureStamp(report, r.ConcurrencyStamp, db);
        if (report.Status == ReportStatus.Published) throw DomainException.Conflict("report.published", "This report is already published.");
        report.Status = ReportStatus.Published;
        report.PublishedAt = Now;
        report.PublishedByUserId = currentUser.Id;
        audit.Record("report.published", nameof(ClientReport), id, new { Status = ReportStatus.Draft }, new { report.Status, report.PeriodStart });
        var members = await db.Set<ClientMember>().AsNoTracking().Where(m => m.ClientAccountId == report.ClientAccountId).Select(m => m.UserId).ToListAsync(ct);
        foreach (var u in members)
            await notifications.StageAsync(new NotificationRequest(u, DeliveryNotificationTypes.ReportPublished,
                $"Your {report.PeriodStart:MMMM yyyy} report is ready", report.Title, DeliveryLinks.ClientReport(report.ClientAccountId, id),
                r.NotifyByEmail ? new[] { NotificationChannel.Email } : null), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var report = await LoadAsync(id, ct);
        if (report.Status == ReportStatus.Published) throw DomainException.Conflict("report.published", "Published reports can't be deleted.");
        db.Remove(report);
        audit.Record("report.deleted", nameof(ClientReport), id, before: new { report.Title, report.PeriodStart });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ client portal

    public async Task<IReadOnlyList<ReportSummaryDto>> ClientListAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var rows = await db.Set<ClientReport>().AsNoTracking().Where(r => r.ClientAccountId == clientId && r.Status == ReportStatus.Published)
            .OrderByDescending(r => r.PeriodStart).Take(60).ToListAsync(ct);
        return await SummariesAsync(rows, ct);
    }

    public async Task<ReportDto> ClientGetAsync(Guid clientId, Guid id, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var r = await db.Set<ClientReport>().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id && x.ClientAccountId == clientId && x.Status == ReportStatus.Published, ct)
                ?? throw DomainException.NotFound("Report");
        return (await ToDtoAsync(r, ct)) with { AvailableProviders = Array.Empty<ReportProviderDto>() };
    }

    // ------------------------------------------------------------------ job

    /// <summary>
    /// Creates last month's draft for every Active client with an active retainer-type project. Idempotent per client and
    /// month (unique AutoKey); a concurrent or repeated run skips existing drafts.
    /// </summary>
    public async Task<int> GenerateMonthlyDraftsAsync(CancellationToken ct)
    {
        var now = Now;
        var period = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
        var retainerTypes = new[] { ProjectType.RetainerMonth, ProjectType.SeoProgram, ProjectType.SocialContent };
        var candidates = await (from p in db.Set<Project>().AsNoTracking()
                                join c in db.Set<ClientAccount>() on p.ClientAccountId equals c.Id
                                where c.Status == ClientAccountStatus.Active && p.Status == ProjectStatus.Active && retainerTypes.Contains(p.Type)
                                select new { ClientId = c.Id, ProjectId = p.Id }).ToListAsync(ct);
        var created = 0;
        foreach (var group in candidates.GroupBy(c => c.ClientId))
        {
            var key = $"{group.Key}:{period:yyyy-MM}";
            if (await db.Set<ClientReport>().AnyAsync(r => r.AutoKey == key, ct)) continue;
            var draft = await BuildDraftAsync(group.Key, group.Count() == 1 ? group.First().ProjectId : null, period, DefaultTemplateKey, null, null, key, ct);
            db.Set<ClientReport>().Add(draft);
            try
            {
                await db.SaveChangesAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                db.Entry(draft).State = EntityState.Detached;
            }
        }
        return created;
    }
}
