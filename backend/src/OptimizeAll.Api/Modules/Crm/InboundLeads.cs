using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>A normalized inbound lead from any source (website inquiry, landing-page form).</summary>
public sealed record InboundLead(
    string Key, DealSource Source, string? SourceDetail, string? Name, string Email, string? Phone, string? Company, string? Website,
    string? Message, IReadOnlyList<string> ServiceSlugs, string? BudgetRange, string? UtmSource, string? UtmMedium, string? UtmCampaign,
    string EngagementType, DateTime OccurredAt);

/// <summary>
/// Turns inbound leads into CRM records: creates or merges the contact (dedupe by normalized email) and company (dedupe by
/// domain), opens a deal in the first pipeline stage (or reuses the contact's open deal) with source and first/last-touch
/// UTM attribution, assigns an owner by round-robin over active Sales reps and Account managers, notifies them and
/// re-scores the contact. Idempotent: each event key is processed once (<see cref="CrmInboundEvent"/>), and processing is
/// serialized on the round-robin cursor row so concurrent leads can neither duplicate records nor share a turn.
/// </summary>
public sealed class InboundLeadService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IAuditLogger audit,
    INotificationService notifications,
    LeadScoringService scoring,
    BillingSettingsService billingSettings,
    TimeProvider clock,
    ILogger<InboundLeadService> logger)
{
    public const string InboundPool = "inbound";

    /// <summary>
    /// Inbound leads go round-robin to active users who hold <c>crm.manage</c> through a job role: a built-in role other
    /// than Admin (SalesRep, AccountManager) or a custom role. Admins (who hold every permission) are assigned leads only
    /// when they also hold such a role, so granting someone Admin never silently puts them into the lead rotation.
    /// </summary>
    public static readonly string[] AssignablePermissions = { Permissions.CrmManage };

    public async Task<CrmInboundEvent?> ProcessAsync(InboundLead lead, CancellationToken ct)
    {
        var email = lead.Email.Trim();
        if (!CrmNormalization.IsValidEmail(email))
        {
            logger.LogInformation("Inbound lead {Key} skipped: no valid email", lead.Key);
            return null;
        }
        for (var attempt = 1; ; attempt++)
        {
            var done = await db.Set<CrmInboundEvent>().AsNoTracking().FirstOrDefaultAsync(e => e.Key == lead.Key, ct);
            if (done is not null) return done;
            try
            {
                var result = await ProcessOnceAsync(lead, email, ct);
                if (result.ContactId is { } contactId)
                {
                    await scoring.RecordEngagementAsync(contactId, lead.EngagementType, $"{lead.EngagementType}:{lead.Key}", lead.OccurredAt, ct);
                    await scoring.RecomputeAsync(contactId, ct);
                }
                return result;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex) && attempt < 3)
            {
                // A concurrent event created the same contact/company/key first: retry against the new state.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<CrmInboundEvent> ProcessOnceAsync(InboundLead lead, string email, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cursorId = await EnsureCursorAsync(ct);
        var stage = await CrmService.FirstStageAsync(db, ct);
        var currency = (await billingSettings.GetAsync(ct)).DefaultCurrency;
        var normalized = Normalization.Email(email);
        var touch = new UtmTouch { Source = CrmService.Trim(lead.UtmSource, 100), Medium = CrmService.Trim(lead.UtmMedium, 100),
            Campaign = CrmService.Trim(lead.UtmCampaign, 150), At = lead.OccurredAt };

        CrmInboundEvent record;
        Guid? notifyUser = null;
        CrmDeal? newDeal = null;
        string contactName;
        string? companyName;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            await dialect.LockRowAsync(db, "crm_assignment_cursors", cursorId, ct);
            if (await db.Set<CrmInboundEvent>().AnyAsync(e => e.Key == lead.Key, ct))
                return await db.Set<CrmInboundEvent>().AsNoTracking().FirstAsync(e => e.Key == lead.Key, ct);

            // ---- Company (dedupe by domain; else by name among companies without a domain)
            var domain = CrmNormalization.Domain(lead.Website) ?? CrmNormalization.EmailDomain(email);
            var companyLabel = CrmService.Trim(lead.Company, 200);
            CrmCompany? company = null;
            if (domain is not null) company = await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Domain == domain, ct);
            if (company is null && companyLabel is not null)
                company = await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Domain == null && c.Name == companyLabel, ct);
            if (company is null && (companyLabel is not null || domain is not null))
            {
                company = new CrmCompany { Name = companyLabel ?? domain!, Domain = domain };
                db.Set<CrmCompany>().Add(company);
            }
            else if (company is not null && company.Domain is null && domain is not null)
            {
                company.Domain = domain;
            }
            // A new inquiry brings an archived company or contact back into the active lists.
            if (company is not null) company.ArchivedAt = null;

            // ---- Contact (dedupe by normalized email; fill blanks, never overwrite what the team entered)
            var contact = await db.Set<CrmContact>().FirstOrDefaultAsync(c => c.NormalizedEmail == normalized, ct);
            var (first, last) = CrmNormalization.SplitName(lead.Name, email.Split('@')[0]);
            if (contact is null)
            {
                contact = new CrmContact
                {
                    FirstName = CrmService.Trim(first, 100)!, LastName = CrmService.Trim(last, 100), Email = email, NormalizedEmail = normalized,
                    LifecycleStage = LifecycleStage.Lead, Source = lead.Source.ToString(), FirstTouch = Copy(touch), LastTouch = Copy(touch),
                };
                db.Set<CrmContact>().Add(contact);
            }
            else
            {
                contact.LastTouch = Copy(touch);
                if (contact.FirstTouch.IsEmpty && contact.FirstTouch.At is null) contact.FirstTouch = Copy(touch);
                if (contact.LifecycleStage == LifecycleStage.Subscriber) contact.LifecycleStage = LifecycleStage.Lead;
                contact.ArchivedAt = null;
            }
            contact.Phone ??= CrmService.Trim(lead.Phone, 40);
            contact.BudgetRange ??= CrmService.Trim(lead.BudgetRange, 60);
            contact.CompanyId ??= company?.Id;
            contactName = contact.DisplayName;
            companyName = company?.Name;

            // ---- Owner: keep an eligible existing owner, otherwise the next person in the round-robin
            var owner = contact.OwnerUserId is { } existingOwner && await IsAssignableAsync(existingOwner, ct)
                ? existingOwner
                : await NextAssigneeAsync(cursorId, now, ct);
            contact.OwnerUserId ??= owner;
            if (company is not null) company.OwnerUserId ??= owner;

            // ---- Deal: reuse the contact's open deal, otherwise open a new one in the first stage
            var deal = await db.Set<CrmDeal>().Where(d => d.PrimaryContactId == contact.Id && d.Status == DealStatus.Open)
                .OrderByDescending(d => d.CreatedAt).FirstOrDefaultAsync(ct);
            var services = lead.ServiceSlugs.Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length is > 0 and <= 100 && s.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-'))
                .Distinct().Take(20).ToList();
            if (deal is null)
            {
                deal = new CrmDeal
                {
                    Title = $"{companyName ?? contactName} — {Humanize(lead.SourceDetail) ?? lead.Source.ToString()}",
                    CompanyId = company?.Id, PrimaryContactId = contact.Id, StageId = stage.Id, StageChangedAt = now, Currency = currency,
                    ServiceSlugs = services, OwnerUserId = owner, Source = lead.Source, SourceDetail = CrmService.Trim(lead.SourceDetail, 100),
                    BudgetRange = CrmService.Trim(lead.BudgetRange, 60), FirstTouch = contact.FirstTouch.IsEmpty ? Copy(touch) : Copy(contact.FirstTouch),
                    LastTouch = Copy(touch),
                };
                if (deal.Title.Length > 200) deal.Title = deal.Title[..200];
                db.Set<CrmDeal>().Add(deal);
                newDeal = deal;
                notifyUser = owner;
            }
            else
            {
                deal.LastTouch = Copy(touch);
                deal.ServiceSlugs = deal.ServiceSlugs.Union(services).Take(20).ToList();
                notifyUser = deal.OwnerUserId;
            }
            db.Set<CrmActivity>().Add(new CrmActivity
            {
                Type = ActivityType.Note, IsSystem = true, DealId = deal.Id, ContactId = contact.Id, CompanyId = company?.Id,
                Subject = $"{Humanize(lead.SourceDetail) ?? lead.Source.ToString()} received",
                Body = CrmService.Trim(string.Join("\n", new[]
                {
                    lead.Message,
                    services.Count > 0 ? $"Services: {string.Join(", ", services)}" : null,
                    lead.BudgetRange is null ? null : $"Budget: {lead.BudgetRange}",
                    touch.IsEmpty ? null : $"UTM: {touch.Source}/{touch.Medium}/{touch.Campaign}",
                }.Where(s => !string.IsNullOrWhiteSpace(s))), 10000),
                OccursAt = lead.OccurredAt,
            });

            record = new CrmInboundEvent
            {
                Key = lead.Key, ContactId = contact.Id, CompanyId = company?.Id, DealId = deal.Id, AssignedUserId = deal.OwnerUserId, ProcessedAt = now,
            };
            db.Set<CrmInboundEvent>().Add(record);
            audit.RecordSystem("crm.inbound_lead", nameof(CrmDeal), deal.Id,
                new { lead.Key, lead.Source, ContactId = contact.Id, CompanyId = company?.Id, NewDeal = newDeal is not null, Owner = deal.OwnerUserId });

            if (notifyUser is { } assignee)
                await notifications.StageAsync(new NotificationRequest(assignee, BillingNotificationTypes.LeadAssigned,
                    newDeal is not null ? $"New lead: {companyName ?? contactName}" : $"{contactName} got in touch again",
                    $"{contactName} ({email}) — {Humanize(lead.SourceDetail) ?? lead.Source.ToString()}. " +
                    (newDeal is not null ? "A new deal was added to your pipeline." : "Logged on the existing deal."),
                    BillingLinks.AgencyDeal(deal.Id), new[] { NotificationChannel.Email }), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return record;
    }

    private async Task<Guid> EnsureCursorAsync(CancellationToken ct)
    {
        var id = await db.Set<CrmAssignmentCursor>().AsNoTracking().Where(c => c.Pool == InboundPool).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (id is { } existing) return existing;
        var cursor = new CrmAssignmentCursor { Pool = InboundPool, UpdatedAt = clock.GetUtcNow().UtcDateTime };
        db.Set<CrmAssignmentCursor>().Add(cursor);
        try
        {
            await db.SaveChangesAsync(ct);
            return cursor.Id;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return await db.Set<CrmAssignmentCursor>().AsNoTracking().Where(c => c.Pool == InboundPool).Select(c => c.Id).FirstAsync(ct);
        }
    }

    private async Task<IQueryable<User>> AssignableUsersAsync(CancellationToken ct) =>
        (await new PermissionDirectory(db).WorkersWithAnyPermissionAsync(AssignablePermissions, ct)).Where(u => u.Status == UserStatus.Active);

    private async Task<bool> IsAssignableAsync(Guid userId, CancellationToken ct) =>
        await (await AssignableUsersAsync(ct)).AnyAsync(u => u.Id == userId, ct);

    /// <summary>The next eligible user after the cursor (ordered by id, wrapping around). Caller holds the cursor row lock.</summary>
    private async Task<Guid?> NextAssigneeAsync(Guid cursorId, DateTime now, CancellationToken ct)
    {
        var candidates = await (await AssignableUsersAsync(ct)).Select(u => u.Id).ToListAsync(ct);
        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));
        var cursor = await db.Set<CrmAssignmentCursor>().FirstAsync(c => c.Id == cursorId, ct);
        var next = cursor.LastUserId is { } last
            ? candidates.FirstOrDefault(c => string.CompareOrdinal(c.ToString(), last.ToString()) > 0)
            : Guid.Empty;
        if (next == Guid.Empty) next = candidates[0];
        cursor.LastUserId = next;
        cursor.UpdatedAt = now;
        return next;
    }

    private static UtmTouch Copy(UtmTouch t) => new() { Source = t.Source, Medium = t.Medium, Campaign = t.Campaign, At = t.At };

    private static string? Humanize(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? null : char.ToUpperInvariant(slug.Trim()[0]) + slug.Trim()[1..].Replace('-', ' ').Replace('_', ' ');
}

/// <summary>Website contact / audit / quote / consultation forms → CRM lead.</summary>
public sealed class WebsiteInquiryLeadHandler(InboundLeadService inbound) : IEventHandler<WebsiteInquiryReceived>
{
    public Task HandleAsync(WebsiteInquiryReceived e, CancellationToken ct) =>
        inbound.ProcessAsync(new InboundLead($"inquiry:{e.InquiryId}", DealSource.WebsiteInquiry, e.InquiryType, e.Name, e.Email, e.Phone,
            e.Company, e.Website, e.Message, e.ServiceSlugs, e.BudgetRange, e.UtmSource, e.UtmMedium, e.UtmCampaign, "website_inquiry",
            e.OccurredAt), ct);
}

/// <summary>
/// Agency landing-page forms → CRM lead. Forms owned by a client (ClientAccountId set) collect the client's own leads and
/// are not part of the agency's sales pipeline, so they are ignored here.
/// </summary>
public sealed class FormSubmittedLeadHandler(InboundLeadService inbound) : IEventHandler<FormSubmitted>
{
    public Task HandleAsync(FormSubmitted e, CancellationToken ct)
    {
        if (e.ClientAccountId is not null || string.IsNullOrWhiteSpace(e.Email)) return Task.CompletedTask;
        string? Field(params string[] keys) =>
            keys.Select(k => e.Fields.FirstOrDefault(f => string.Equals(f.Key, k, StringComparison.OrdinalIgnoreCase)).Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        var name = e.Name ?? string.Join(' ', new[] { Field("first_name", "firstName"), Field("last_name", "lastName") }.Where(s => s is not null));
        var services = (Field("services", "service") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return inbound.ProcessAsync(new InboundLead($"form:{e.SubmissionId}", DealSource.Form, Field("form_name", "formName") ?? "landing-page-form",
            name, e.Email, e.Phone ?? Field("phone"), Field("company", "company_name"), Field("website", "url"), Field("message", "comments"),
            services, Field("budget", "budget_range"), e.UtmSource, e.UtmMedium, e.UtmCampaign, "form_submitted", e.OccurredAt), ct);
    }
}
