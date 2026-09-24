using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

public static class LeadReference
{
    /// <summary>Short human reference for a lead or booking ("OA-7F3A91C2"); ids are time-ordered, so use the random tail.</summary>
    public static string For(Guid id) => "OA-" + id.ToString("N")[^8..].ToUpperInvariant();
}

/// <summary>Validated contact details shared by every form.</summary>
internal sealed record ContactDetails(string Name, string Email, string? Phone, string? Company, string? Website);

/// <summary>
/// Public lead capture (contact, free audit, quote) and the staff inquiry inbox. Each accepted submission is stored as a
/// <see cref="WebsiteInquiry"/> and published once as <see cref="WebsiteInquiryReceived"/> after the transaction commits
/// (the CRM creates leads from it; this module notifies staff).
/// </summary>
public sealed class InquiryService(
    AppDbContext db, FormGuard guard, IPrivacyHasher hasher, ICurrentUser user, IEventPublisher events, IAuditLogger audit, TimeProvider clock)
{
    public const string SpamMessage = "Thanks! We've received your message and will reply within one business day.";

    public Task<InquiryAcceptedDto> ContactAsync(ContactInquiryInput input, CancellationToken ct) =>
        SubmitAsync(InquiryType.Contact, input, async (inquiry, e) =>
        {
            inquiry.Message = input.Message.Trim();
            inquiry.ServiceSlugs = await ServiceSlugsAsync(input.ServiceSlugs, e, required: false, ct);
        }, ct);

    public Task<InquiryAcceptedDto> AuditAsync(AuditInquiryInput input, CancellationToken ct) =>
        SubmitAsync(InquiryType.Audit, input, async (inquiry, e) =>
        {
            if (inquiry.Website is null) e.Add("website", "Enter the website you'd like us to audit.");
            inquiry.BudgetRange = Option(input.BudgetRange, FormGuard.BudgetRanges, "budgetRange", e);
            inquiry.ServiceSlugs = await ServiceSlugsAsync(input.ServiceSlugs, e, required: true, ct);
            inquiry.Message = WebsiteRules.Clean(input.Message);
            inquiry.PayloadJson = Payload(new Dictionary<string, string?>
            {
                ["goals"] = input.Goals.Trim(),
                ["competitors"] = WebsiteRules.Clean(input.Competitors),
            });
        }, ct);

    public Task<InquiryAcceptedDto> QuoteAsync(QuoteInquiryInput input, CancellationToken ct) =>
        SubmitAsync(InquiryType.Quote, input, async (inquiry, e) =>
        {
            inquiry.BudgetRange = Option(input.BudgetRange, FormGuard.BudgetRanges, "budgetRange", e);
            inquiry.Timeline = Option(input.Timeline, FormGuard.Timelines, "timeline", e);
            inquiry.Message = input.Message.Trim();
            var packageIds = (input.PackageIds ?? new List<Guid>()).Distinct().ToList();
            if (packageIds.Count > 20) e.Add("packageIds", "Pick at most 20 packages.");
            var packages = packageIds.Count == 0 ? new List<(Guid Id, string Slug)>() :
                (await (from p in db.Set<ServicePackage>().AsNoTracking()
                        join s in db.Set<AgencyService>() on p.ServiceId equals s.Id
                        where packageIds.Contains(p.Id) && p.IsActive && s.IsPublished
                        select new { p.Id, s.Slug }).ToListAsync(ct)).Select(x => (x.Id, x.Slug)).ToList();
            if (packages.Count != packageIds.Count) e.Add("packageIds", "Some packages are no longer available.");
            var slugs = await ServiceSlugsAsync(input.ServiceSlugs, e, required: packages.Count == 0, ct);
            inquiry.PackageIds = packageIds;
            inquiry.ServiceSlugs = slugs.Union(packages.Select(p => p.Slug)).Distinct().ToList();
        }, ct);

    private async Task<InquiryAcceptedDto> SubmitAsync<TInput>(
        InquiryType type, TInput input, Func<WebsiteInquiry, FieldErrors, Task> fill, CancellationToken ct) where TInput : ContactDetailsInput
    {
        if (guard.Check(input, ConsentTexts.FormVersion) == FormCheck.Spam)
            return new InquiryAcceptedDto(LeadReference.For(Guid.NewGuid()), SpamMessage);

        var e = new FieldErrors();
        var contact = ValidateContact(input, e);
        var inquiry = NewInquiry(type, contact, input);
        await fill(inquiry, e);
        e.ThrowIfAny();

        db.Set<WebsiteInquiry>().Add(inquiry);
        await db.SaveChangesAsync(ct);
        await PublishAsync(inquiry, ct);
        return new InquiryAcceptedDto(LeadReference.For(inquiry.Id), SpamMessage);
    }

    internal WebsiteInquiry NewInquiry(InquiryType type, ContactDetails contact, PublicFormInput input)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new WebsiteInquiry
        {
            Type = type,
            Status = InquiryStatus.New,
            Name = contact.Name,
            Email = contact.Email,
            Phone = contact.Phone,
            Company = contact.Company,
            Website = contact.Website,
            UtmSource = Utm(input.Utm?.Source),
            UtmMedium = Utm(input.Utm?.Medium),
            UtmCampaign = Utm(input.Utm?.Campaign),
            UtmTerm = Utm(input.Utm?.Term),
            UtmContent = Utm(input.Utm?.Content),
            Referrer = SafeReferrer(input.Referrer),
            LandingPath = SafePath(input.LandingPath),
            ConsentVersion = input.ConsentVersion,
            ConsentAt = now,
            IpHash = hasher.Hash(user.IpAddress),
        };
    }

    /// <summary>Publishes <see cref="WebsiteInquiryReceived"/> (call after commit).</summary>
    internal Task PublishAsync(WebsiteInquiry i, CancellationToken ct) =>
        events.PublishAsync(new WebsiteInquiryReceived(i.Id, i.Type.ToString(), i.Name, i.Email, i.Phone, i.Company, i.Website, i.Message,
            i.ServiceSlugs, i.BudgetRange, i.UtmSource, i.UtmMedium, i.UtmCampaign, i.Referrer, clock.GetUtcNow().UtcDateTime), ct);

    internal static ContactDetails ValidateContact(ContactDetailsInput input, FieldErrors e)
    {
        var name = input.Name.Trim();
        if (name.Length < 2) e.Add("name", "Enter your name.");
        var email = input.Email.Trim();
        if (!FieldRules.IsEmail(email)) e.Add("email", "Enter a valid email address, e.g. name@company.com.");
        var phone = WebsiteRules.Clean(input.Phone);
        if (phone is not null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9][0-9 ().-]{5,30}$"))
            e.Add("phone", "Enter a phone number with digits, spaces or dashes, e.g. +1 415 555 0100.");
        var website = NormalizeWebsite(input.Website, e);
        return new ContactDetails(name, email, phone, WebsiteRules.Clean(input.Company), website);
    }

    /// <summary>Accepts "example.com" or a full http(s) URL; stores an absolute URL.</summary>
    internal static string? NormalizeWebsite(string? value, FieldErrors e)
    {
        var v = WebsiteRules.Clean(value);
        if (v is null) return null;
        if (!v.Contains("://", StringComparison.Ordinal)) v = "https://" + v;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !uri.Host.Contains('.') || !string.IsNullOrEmpty(uri.UserInfo))
        {
            e.Add("website", "Enter a website address such as example.com.");
            return null;
        }
        return uri.ToString().TrimEnd('/');
    }

    internal async Task<List<string>> ServiceSlugsAsync(List<string>? slugs, FieldErrors e, bool required, CancellationToken ct)
    {
        var list = (slugs ?? new List<string>()).Select(s => s?.Trim() ?? string.Empty).Where(s => s.Length > 0).Distinct().ToList();
        if (list.Count > 20) e.Add("serviceSlugs", "Pick at most 20 services.");
        if (required && list.Count == 0) e.Add("serviceSlugs", "Pick at least one service you're interested in.");
        if (list.Count > 0)
        {
            var known = await db.Set<AgencyService>().AsNoTracking().Where(s => s.IsPublished && list.Contains(s.Slug)).Select(s => s.Slug).ToListAsync(ct);
            if (known.Count != list.Count) e.Add("serviceSlugs", "Some of the selected services are no longer offered.");
        }
        return list;
    }

    private static string? Option(string? value, IEnumerable<FormOption> options, string field, FieldErrors e)
    {
        var v = WebsiteRules.Clean(value);
        if (v is null || options.All(o => o.Value != v))
        {
            e.Add(field, "Pick one of the options.");
            return null;
        }
        return v;
    }

    private static string Payload(Dictionary<string, string?> values) =>
        JsonSerializer.Serialize(values.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static string? Utm(string? value)
    {
        var v = WebsiteRules.Clean(value);
        return v is null ? null : v.Length > 150 ? v[..150] : v;
    }

    internal static string? SafeReferrer(string? value)
    {
        var v = WebsiteRules.Clean(value);
        if (v is null || v.Length > 500) return null;
        return Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? v : null;
    }

    internal static string? SafePath(string? value)
    {
        var v = WebsiteRules.Clean(value);
        return v is not null && v.Length <= 500 && FieldRules.IsSafeContentUrl(v) && v.StartsWith('/') ? v : null;
    }

    // ---------------------------------------------------------------- Staff inbox (site.manage; crm.view reads)

    public async Task<PagedResult<InquirySummaryDto>> ListAsync(InquiryQuery query, CancellationToken ct)
    {
        user.RequireAny(Permissions.SiteManage, Permissions.CrmView);
        var q = db.Set<WebsiteInquiry>().AsNoTracking();
        if (query.Type is { } type) q = q.Where(i => i.Type == type);
        if (query.Status is { } status) q = q.Where(i => i.Status == status);
        if (WebsiteRules.Utc(query.From) is { } from) q = q.Where(i => i.CreatedAt >= from);
        if (WebsiteRules.Utc(query.To) is { } to) q = q.Where(i => i.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.UtmSource)) q = q.Where(i => i.UtmSource == query.UtmSource.Trim());
        switch (query.AssignedTo?.Trim())
        {
            case null or "":
                break;
            case "unassigned":
                q = q.Where(i => i.AssignedToUserId == null);
                break;
            case "me":
                var me = user.Id;
                q = q.Where(i => i.AssignedToUserId == me);
                break;
            default:
                if (!Guid.TryParse(query.AssignedTo, out var assignee))
                    throw FieldRules.FieldError("website.invalid", "assignedTo", "Use a user id, \"me\" or \"unassigned\".");
                q = q.Where(i => i.AssignedToUserId == assignee);
                break;
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(i => EF.Functions.Like(i.Name, p) || EF.Functions.Like(i.Email, p) || (i.Company != null && EF.Functions.Like(i.Company, p)));
        }
        q = q.OrderByDescending(i => i.CreatedAt);
        if (!string.IsNullOrWhiteSpace(query.Service))
        {
            // Service slugs live in a JSON list: filter in memory (inbox volumes are small).
            var service = query.Service.Trim();
            var all = (await q.ToListAsync(ct)).Where(i => i.ServiceSlugs.Contains(service)).ToList();
            return new PagedResult<InquirySummaryDto>(all.Skip(query.Skip).Take(query.PageSize).Select(Summary).ToList(), all.Count, query.Page, query.PageSize);
        }
        return CmsStore.Map(await q.ToPagedAsync(query, ct), Summary);
    }

    public async Task<InquiryDto> GetAsync(Guid id, CancellationToken ct)
    {
        user.RequireAny(Permissions.SiteManage, Permissions.CrmView);
        var i = await db.Set<WebsiteInquiry>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<WebsiteInquiry>();
        var bookingId = await db.Set<ConsultationBooking>().AsNoTracking().Where(b => b.InquiryId == id).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        return ToDto(i, bookingId);
    }

    public async Task<InquiryDto> UpdateAsync(Guid id, UpdateInquiryInput input, CancellationToken ct)
    {
        user.Require(Permissions.SiteManage);
        var i = await db.Set<WebsiteInquiry>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<WebsiteInquiry>();
        CmsStore.CheckStamp(db, i, input.ConcurrencyStamp);
        if (input.Status is not { } status || !Enum.IsDefined(status)) throw FieldRules.FieldError("website.invalid", "status", "Pick a status.");
        if (input.AssignedToUserId is { } assignee && !await db.Set<Domain.Identity.User>().AnyAsync(u => u.Id == assignee, ct))
            throw FieldRules.FieldError("website.invalid", "assignedToUserId", "Pick an existing team member.");
        var before = new { i.Status, i.AssignedToUserId, i.StaffNotes };
        i.Status = status;
        i.AssignedToUserId = input.AssignedToUserId;
        i.StaffNotes = WebsiteRules.Clean(input.StaffNotes);
        audit.Record("website.inquiry_updated", nameof(WebsiteInquiry), i.Id, before, new { i.Status, i.AssignedToUserId, i.StaffNotes });
        await db.SaveChangesAsync(ct);
        return ToDto(i, null);
    }

    /// <summary>Erases an inquiry (spam, or a data-erasure request). A linked booking keeps its own record but loses the link.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        user.Require(Permissions.SiteManage);
        var i = await db.Set<WebsiteInquiry>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<WebsiteInquiry>();
        foreach (var b in await db.Set<ConsultationBooking>().Where(b => b.InquiryId == id).ToListAsync(ct)) b.InquiryId = null;
        audit.Record("website.inquiry_deleted", nameof(WebsiteInquiry), i.Id, new { i.Type, i.Status, Reference = LeadReference.For(i.Id) });
        db.Remove(i);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebsiteInquiry>> ExportRowsAsync(InquiryQuery query, CancellationToken ct)
    {
        user.RequireAny(Permissions.SiteManage, Permissions.CrmView);
        var q = db.Set<WebsiteInquiry>().AsNoTracking();
        if (query.Type is { } type) q = q.Where(i => i.Type == type);
        if (query.Status is { } status) q = q.Where(i => i.Status == status);
        return await q.OrderByDescending(i => i.CreatedAt).Take(10000).ToListAsync(ct);
    }

    private static InquirySummaryDto Summary(WebsiteInquiry i) => new(
        i.Id, LeadReference.For(i.Id), i.Type, i.Status, i.Name, i.Email, i.Company, i.ServiceSlugs, i.BudgetRange, i.UtmSource, i.UtmCampaign,
        i.AssignedToUserId, i.CreatedAt);

    private static InquiryDto ToDto(WebsiteInquiry i, Guid? bookingId)
    {
        Dictionary<string, string> details;
        try
        {
            details = JsonSerializer.Deserialize<Dictionary<string, string>>(i.PayloadJson) ?? new();
        }
        catch (JsonException)
        {
            details = new();
        }
        return new InquiryDto(i.Id, LeadReference.For(i.Id), i.Type, i.Status, i.Name, i.Email, i.Phone, i.Company, i.Website, i.Message,
            i.ServiceSlugs, i.PackageIds, i.BudgetRange, i.Timeline, details, i.UtmSource, i.UtmMedium, i.UtmCampaign, i.UtmTerm, i.UtmContent,
            i.Referrer, i.LandingPath, i.ConsentVersion, i.ConsentAt, i.AssignedToUserId, i.StaffNotes, bookingId, i.CreatedAt, i.UpdatedAt,
            i.ConcurrencyStamp);
    }
}
