using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>Companies, contacts, deals, activities, pipeline stages, saved views and the CRM dashboard.</summary>
public sealed class CrmService(
    AppDbContext db,
    IDatabaseDialect dialect,
    ICurrentUser currentUser,
    IAuditLogger audit,
    LeadScoringService scoring,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================ Shared helpers

    public static void RequireStamp<T>(AppDbContext db, T entity, Guid? stamp) where T : class, IConcurrencyStamped
    {
        if (stamp is null || stamp.Value != entity.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This record was changed by someone else. Reload and try again.");
        var stampProperty = db.Entry(entity).Property(e => e.ConcurrencyStamp);
        stampProperty.OriginalValue = stamp.Value;
        // Every accepted edit rotates the stamp (and is checked against it), even if no other column changes.
        stampProperty.IsModified = true;
    }

    /// <summary>Lifecycle stages before Customer (enums are stored as strings, so compare by membership, never with &lt;).</summary>
    public static readonly LifecycleStage[] PreCustomerStages =
    {
        LifecycleStage.Subscriber, LifecycleStage.Lead, LifecycleStage.MarketingQualifiedLead, LifecycleStage.SalesQualifiedLead,
        LifecycleStage.Opportunity,
    };

    public static string TagIndex(IEnumerable<string> tags) => tags.Any() ? "," + string.Join(",", tags) + "," : string.Empty;

    private static string Like(string search) => PagingExtensions.LikePattern(search);

    /// <summary>Roles whose permission set includes <c>crm.view</c> (owners and assignees must hold one).</summary>
    public static readonly Role[] CrmRoles = Enum.GetValues<Role>().Where(r => RolePermissions.For(r).Contains(Permissions.CrmView)).ToArray();

    public async Task<IReadOnlyList<UserRefDto>> AssigneesAsync(CancellationToken ct) =>
        await db.Set<User>().AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => CrmRoles.Contains(r.Role)))
            .OrderBy(u => u.DisplayName).Select(u => new UserRefDto(u.Id, u.DisplayName, u.Email)).ToListAsync(ct);

    private async Task ValidateUserAsync(Guid? userId, string field, CancellationToken ct)
    {
        if (userId is null) return;
        var ok = await db.Set<User>().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active && u.Roles.Any(r => CrmRoles.Contains(r.Role)), ct);
        if (!ok)
            throw new DomainException("crm.invalid_user", "Choose an active team member with CRM access.",
                errors: new Dictionary<string, string[]> { [field] = new[] { "Choose an active team member with CRM access." } });
    }

    private async Task<Dictionary<Guid, UserRefDto>> UsersAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, UserRefDto>();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Id, u.DisplayName, u.Email), ct);
    }

    private static UserRefDto? Ref(Dictionary<Guid, UserRefDto> users, Guid? id) => id is { } v ? users.GetValueOrDefault(v) : null;

    private static UtmDto ToDto(UtmTouch t) => new(t.Source, t.Medium, t.Campaign, t.At);

    private static UtmTouch FromDto(UtmDto? d) => new()
    {
        Source = Trim(d?.Source, 100), Medium = Trim(d?.Medium, 100), Campaign = Trim(d?.Campaign, 150), At = d?.At,
    };

    public static string? Trim(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();

    // ================================================================ Companies

    public async Task<PagedResult<CompanySummaryDto>> ListCompaniesAsync(CompanyQuery q, CancellationToken ct)
    {
        var companies = db.Set<CrmCompany>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
            companies = companies.Where(c => EF.Functions.Like(c.Name, Like(q.Search), "\\") || EF.Functions.Like(c.Domain!, Like(q.Search), "\\"));
        if (q.OwnerUserId is { } owner) companies = companies.Where(c => c.OwnerUserId == owner);
        if (!string.IsNullOrWhiteSpace(q.Industry)) companies = companies.Where(c => c.Industry == q.Industry);
        if (!string.IsNullOrWhiteSpace(q.Tag)) companies = companies.Where(c => c.TagIndex.Contains("," + q.Tag.Trim().ToLower() + ","));
        companies = q.Sort switch
        {
            "name" => q.Desc ? companies.OrderByDescending(c => c.Name) : companies.OrderBy(c => c.Name),
            _ => q.Desc ? companies.OrderByDescending(c => c.CreatedAt) : companies.OrderBy(c => c.CreatedAt),
        };
        var total = await companies.CountAsync(ct);
        var rows = await companies.Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var contacts = await db.Set<CrmContact>().AsNoTracking().Where(c => c.CompanyId != null && ids.Contains(c.CompanyId.Value))
            .GroupBy(c => c.CompanyId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count, ct);
        var deals = await db.Set<CrmDeal>().AsNoTracking().Where(d => d.CompanyId != null && ids.Contains(d.CompanyId.Value) && d.Status == DealStatus.Open)
            .GroupBy(d => d.CompanyId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key!.Value, x => x.Count, ct);
        var users = await UsersAsync(rows.Select(r => r.OwnerUserId), ct);
        return new PagedResult<CompanySummaryDto>(rows.Select(c => new CompanySummaryDto(c.Id, c.Name, c.Domain, c.Industry, c.Size, c.CountryCode,
            Ref(users, c.OwnerUserId), c.Tags, contacts.GetValueOrDefault(c.Id), deals.GetValueOrDefault(c.Id), c.ClientAccountId, c.CreatedAt)).ToList(),
            total, q.Page, q.PageSize);
    }

    public async Task<CompanyDto> GetCompanyAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Set<CrmCompany>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Company");
        var contacts = await ListContactsAsync(new ContactQuery { CompanyId = id, PageSize = 200 }, ct);
        var deals = await ListDealsAsync(new DealQuery { CompanyId = id, PageSize = 200 }, ct);
        var users = await UsersAsync(new[] { c.OwnerUserId }, ct);
        return new CompanyDto(c.Id, c.Name, c.Domain, c.Industry, c.Size, c.CountryCode, Ref(users, c.OwnerUserId), c.Tags, c.CustomFieldsJson,
            c.ClientAccountId, contacts.Items, deals.Items, c.CreatedAt, c.UpdatedAt, c.ConcurrencyStamp);
    }

    public async Task<CompanyDto> CreateCompanyAsync(CompanyRequest r, CancellationToken ct)
    {
        var company = new CrmCompany();
        await ApplyAsync(company, r, ct);
        db.Set<CrmCompany>().Add(company);
        audit.Record("crm.company_created", nameof(CrmCompany), company.Id, after: new { company.Name, company.Domain });
        await SaveUniqueAsync("crm.duplicate_domain", "A company with this domain already exists.", ct);
        return await GetCompanyAsync(company.Id, ct);
    }

    public async Task<CompanyDto> UpdateCompanyAsync(Guid id, CompanyRequest r, CancellationToken ct)
    {
        var company = await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Company");
        RequireStamp(db, company, r.ConcurrencyStamp);
        var before = new { company.Name, company.Domain, company.Industry, company.Size, company.OwnerUserId };
        await ApplyAsync(company, r, ct);
        audit.Record("crm.company_updated", nameof(CrmCompany), id, before,
            new { company.Name, company.Domain, company.Industry, company.Size, company.OwnerUserId });
        await SaveUniqueAsync("crm.duplicate_domain", "A company with this domain already exists.", ct);
        await scoring.RecomputeCompanyAsync(id, ct);
        return await GetCompanyAsync(id, ct);
    }

    private async Task ApplyAsync(CrmCompany c, CompanyRequest r, CancellationToken ct)
    {
        await ValidateUserAsync(r.OwnerUserId, "ownerUserId", ct);
        var domain = CrmNormalization.Domain(r.Domain);
        if (!string.IsNullOrWhiteSpace(r.Domain) && domain is null)
            throw new DomainException("crm.invalid_domain", "Enter a valid domain such as example.com.",
                errors: new Dictionary<string, string[]> { ["domain"] = new[] { "Enter a valid domain such as example.com." } });
        c.Name = r.Name.Trim();
        c.Domain = domain;
        c.Industry = Trim(r.Industry, 100);
        c.Size = r.Size;
        c.CountryCode = string.IsNullOrWhiteSpace(r.CountryCode) ? null : r.CountryCode.Trim().ToUpperInvariant();
        c.OwnerUserId = r.OwnerUserId;
        c.Tags = CrmNormalization.Tags(r.Tags);
        c.TagIndex = TagIndex(c.Tags);
        c.CustomFieldsJson = CustomFields.Normalize(r.CustomFields);
    }

    private async Task SaveUniqueAsync(string code, string message, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict(code, message);
        }
    }

    // ================================================================ Contacts

    public async Task<PagedResult<ContactSummaryDto>> ListContactsAsync(ContactQuery q, CancellationToken ct)
    {
        var contacts = FilterContacts(q);
        contacts = q.Sort switch
        {
            "name" => q.Desc ? contacts.OrderByDescending(c => c.FirstName).ThenByDescending(c => c.LastName) : contacts.OrderBy(c => c.FirstName).ThenBy(c => c.LastName),
            "score" => q.Desc ? contacts.OrderByDescending(c => c.Score) : contacts.OrderBy(c => c.Score),
            _ => q.Desc ? contacts.OrderByDescending(c => c.CreatedAt) : contacts.OrderBy(c => c.CreatedAt),
        };
        var total = await contacts.CountAsync(ct);
        var rows = await contacts.Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<ContactSummaryDto>(await ContactSummariesAsync(rows, ct), total, q.Page, q.PageSize);
    }

    public IQueryable<CrmContact> FilterContacts(ContactQuery q)
    {
        var contacts = db.Set<CrmContact>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = Like(q.Search);
            contacts = contacts.Where(c => EF.Functions.Like(c.FirstName, p, "\\") || EF.Functions.Like(c.LastName!, p, "\\") ||
                                           EF.Functions.Like(c.Email!, p, "\\") || EF.Functions.Like(c.Phone!, p, "\\"));
        }
        if (q.LifecycleStage is { } stage) contacts = contacts.Where(c => c.LifecycleStage == stage);
        if (q.OwnerUserId is { } owner) contacts = contacts.Where(c => c.OwnerUserId == owner);
        if (q.CompanyId is { } company) contacts = contacts.Where(c => c.CompanyId == company);
        if (q.ConsentStatus is { } consent) contacts = contacts.Where(c => c.ConsentStatus == consent);
        if (q.MinScore is { } min) contacts = contacts.Where(c => c.Score >= min);
        if (!string.IsNullOrWhiteSpace(q.Tag)) contacts = contacts.Where(c => c.TagIndex.Contains("," + q.Tag.Trim().ToLower() + ","));
        return contacts;
    }

    public async Task<IReadOnlyList<ContactSummaryDto>> ContactSummariesAsync(IReadOnlyList<CrmContact> rows, CancellationToken ct)
    {
        var companyIds = rows.Where(r => r.CompanyId.HasValue).Select(r => r.CompanyId!.Value).Distinct().ToList();
        var companies = await db.Set<CrmCompany>().AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var users = await UsersAsync(rows.Select(r => r.OwnerUserId), ct);
        return rows.Select(c => new ContactSummaryDto(c.Id, c.FirstName, c.LastName, c.DisplayName, c.Email, c.Phone, c.JobTitle, c.CompanyId,
            c.CompanyId is { } cid ? companies.GetValueOrDefault(cid) : null, c.LifecycleStage, Ref(users, c.OwnerUserId), c.ConsentStatus, c.Tags,
            c.Source, c.Score, c.CreatedAt)).ToList();
    }

    public async Task<ContactDto> GetContactAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Set<CrmContact>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Contact");
        var companyName = c.CompanyId is { } cid ? await db.Set<CrmCompany>().Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var users = await UsersAsync(new[] { c.OwnerUserId }, ct);
        var score = await scoring.ComputeAsync(id, ct);
        var deals = await ListDealsAsync(new DealQuery { ContactId = id, PageSize = 200 }, ct);
        return new ContactDto(c.Id, c.FirstName, c.LastName, c.DisplayName, c.Email, c.Phone, c.JobTitle, c.CompanyId, companyName, c.LifecycleStage,
            Ref(users, c.OwnerUserId), c.ConsentStatus, c.ConsentChangedAt, c.Tags, c.Source, c.BudgetRange, c.Score,
            score.Lines.Select(l => new ScoreLineDto(l.Rule, l.Category, l.Points)).ToList(), ToDto(c.FirstTouch), ToDto(c.LastTouch), deals.Items,
            await scoring.EngagementCountsAsync(id, ct), c.CreatedAt, c.UpdatedAt, c.ConcurrencyStamp);
    }

    public async Task<ContactDto> CreateContactAsync(ContactRequest r, CancellationToken ct)
    {
        var contact = new CrmContact();
        await ApplyAsync(contact, r, ct);
        db.Set<CrmContact>().Add(contact);
        audit.Record("crm.contact_created", nameof(CrmContact), contact.Id, after: new { contact.Email, contact.LifecycleStage });
        await SaveUniqueAsync("crm.duplicate_email", "A contact with this email already exists.", ct);
        await scoring.RecomputeAsync(contact.Id, ct);
        return await GetContactAsync(contact.Id, ct);
    }

    public async Task<ContactDto> UpdateContactAsync(Guid id, ContactRequest r, CancellationToken ct)
    {
        var contact = await db.Set<CrmContact>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Contact");
        RequireStamp(db, contact, r.ConcurrencyStamp);
        var before = new { contact.Email, contact.LifecycleStage, contact.OwnerUserId, contact.ConsentStatus, contact.CompanyId };
        await ApplyAsync(contact, r, ct);
        audit.Record("crm.contact_updated", nameof(CrmContact), id, before,
            new { contact.Email, contact.LifecycleStage, contact.OwnerUserId, contact.ConsentStatus, contact.CompanyId });
        await SaveUniqueAsync("crm.duplicate_email", "A contact with this email already exists.", ct);
        await scoring.RecomputeAsync(id, ct);
        db.ChangeTracker.Clear();
        return await GetContactAsync(id, ct);
    }

    private async Task ApplyAsync(CrmContact c, ContactRequest r, CancellationToken ct)
    {
        await ValidateUserAsync(r.OwnerUserId, "ownerUserId", ct);
        if (r.CompanyId is { } companyId && !await db.Set<CrmCompany>().AnyAsync(x => x.Id == companyId, ct))
            throw new DomainException("crm.invalid_company", "That company doesn't exist.",
                errors: new Dictionary<string, string[]> { ["companyId"] = new[] { "Choose an existing company." } });
        var email = Trim(r.Email, 254);
        if (email is not null && !CrmNormalization.IsValidEmail(email))
            throw new DomainException("crm.invalid_email", "Enter a valid email address.",
                errors: new Dictionary<string, string[]> { ["email"] = new[] { "Enter a valid email address." } });
        c.FirstName = r.FirstName.Trim();
        c.LastName = Trim(r.LastName, 100);
        c.Email = email;
        c.NormalizedEmail = email is null ? null : Normalization.Email(email);
        c.Phone = Trim(r.Phone, 40);
        c.JobTitle = Trim(r.JobTitle, 120);
        c.CompanyId = r.CompanyId;
        c.LifecycleStage = r.LifecycleStage;
        c.OwnerUserId = r.OwnerUserId;
        if (c.ConsentStatus != r.ConsentStatus) c.ConsentChangedAt = Now;
        c.ConsentStatus = r.ConsentStatus;
        c.Tags = CrmNormalization.Tags(r.Tags);
        c.TagIndex = TagIndex(c.Tags);
        c.Source = Trim(r.Source, 100);
        c.BudgetRange = Trim(r.BudgetRange, 60);
    }

    // ================================================================ Pipeline stages

    public async Task<IReadOnlyList<StageDto>> StagesAsync(CancellationToken ct, bool includeInactive = true)
    {
        var stages = db.Set<PipelineStage>().AsNoTracking();
        if (!includeInactive) stages = stages.Where(s => s.IsActive);
        return await stages.OrderBy(s => s.Position)
            .Select(s => new StageDto(s.Id, s.Name, s.Position, s.WinProbability, s.Kind, s.IsActive, s.ConcurrencyStamp)).ToListAsync(ct);
    }

    /// <summary>First active open stage (where new deals start).</summary>
    public static async Task<PipelineStage> FirstStageAsync(AppDbContext db, CancellationToken ct) =>
        await db.Set<PipelineStage>().AsNoTracking().Where(s => s.IsActive && s.Kind == StageKind.Open).OrderBy(s => s.Position).FirstOrDefaultAsync(ct)
        ?? throw DomainException.Conflict("crm.no_pipeline", "The sales pipeline has no open stage. Configure the pipeline first.");

    public async Task<IReadOnlyList<StageDto>> UpdatePipelineAsync(UpdatePipelineRequest request, CancellationToken ct)
    {
        var input = request.Stages;
        if (input.Count(s => s.Kind == StageKind.Won) != 1 || input.Count(s => s.Kind == StageKind.Lost) != 1)
            throw new DomainException("crm.invalid_pipeline", "The pipeline needs exactly one Won stage and one Lost stage.");
        if (!input.Any(s => s.Kind == StageKind.Open && s.IsActive))
            throw new DomainException("crm.invalid_pipeline", "The pipeline needs at least one active open stage.");
        if (input.Where(s => s.Kind != StageKind.Open).Any(s => !s.IsActive))
            throw new DomainException("crm.invalid_pipeline", "The Won and Lost stages can't be deactivated.");
        if (input.Select(s => s.Name.Trim().ToLowerInvariant()).Distinct().Count() != input.Count)
            throw new DomainException("crm.invalid_pipeline", "Stage names must be unique.");

        var existing = await db.Set<PipelineStage>().ToListAsync(ct);
        var keptIds = input.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
        if (keptIds.Any(id => existing.All(e => e.Id != id)))
            throw DomainException.NotFound("Stage");
        var removed = existing.Where(e => !keptIds.Contains(e.Id)).ToList();
        foreach (var stage in removed)
            if (await db.Set<CrmDeal>().AnyAsync(d => d.StageId == stage.Id, ct))
                throw DomainException.Conflict("crm.stage_in_use",
                    $"The stage \"{stage.Name}\" still has deals. Move them or deactivate the stage instead of removing it.");
        foreach (var s in input.Where(s => s.Id.HasValue))
        {
            var stage = existing.First(e => e.Id == s.Id);
            if (stage.Kind != s.Kind && await db.Set<CrmDeal>().AnyAsync(d => d.StageId == stage.Id, ct))
                throw DomainException.Conflict("crm.stage_in_use", $"The stage \"{stage.Name}\" has deals, so its kind can't change.");
            if (!s.IsActive && stage.IsActive && await db.Set<CrmDeal>().AnyAsync(d => d.StageId == stage.Id && d.Status == DealStatus.Open, ct))
                throw DomainException.Conflict("crm.stage_in_use", $"Move the open deals out of \"{stage.Name}\" before deactivating it.");
        }
        var before = existing.OrderBy(e => e.Position).Select(e => new { e.Name, e.Kind, e.WinProbability, e.IsActive }).ToList();

        db.RemoveRange(removed);
        for (var i = 0; i < input.Count; i++)
        {
            var s = input[i];
            var stage = s.Id is { } id ? existing.First(e => e.Id == id) : db.Set<PipelineStage>().Add(new PipelineStage()).Entity;
            stage.Name = s.Name.Trim();
            stage.WinProbability = s.Kind switch { StageKind.Won => 100, StageKind.Lost => 0, _ => s.WinProbability };
            stage.Kind = s.Kind;
            stage.IsActive = s.IsActive;
            // Won/Lost always sort last.
            stage.Position = s.Kind switch { StageKind.Won => 1000, StageKind.Lost => 1001, _ => (i + 1) * 10 };
        }
        audit.Record("crm.pipeline_updated", nameof(PipelineStage), "pipeline", before,
            input.Select(s => new { s.Name, s.Kind, s.WinProbability, s.IsActive }).ToList());
        await db.SaveChangesAsync(ct);
        return await StagesAsync(ct);
    }

    // ================================================================ Deals

    public async Task<PagedResult<DealSummaryDto>> ListDealsAsync(DealQuery q, CancellationToken ct)
    {
        var deals = db.Set<CrmDeal>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = Like(q.Search);
            var companyIds = db.Set<CrmCompany>().Where(c => EF.Functions.Like(c.Name, p, "\\")).Select(c => c.Id);
            deals = deals.Where(d => EF.Functions.Like(d.Title, p, "\\") || (d.CompanyId != null && companyIds.Contains(d.CompanyId.Value)));
        }
        if (q.StageId is { } stage) deals = deals.Where(d => d.StageId == stage);
        if (q.Status is { } status) deals = deals.Where(d => d.Status == status);
        if (q.OwnerUserId is { } owner) deals = deals.Where(d => d.OwnerUserId == owner);
        if (q.Source is { } source) deals = deals.Where(d => d.Source == source);
        if (q.CompanyId is { } company) deals = deals.Where(d => d.CompanyId == company);
        if (q.ContactId is { } contact)
        {
            var linked = db.Set<CrmDealContact>().Where(x => x.ContactId == contact).Select(x => x.DealId);
            deals = deals.Where(d => d.PrimaryContactId == contact || linked.Contains(d.Id));
        }
        deals = q.Sort switch
        {
            "title" => q.Desc ? deals.OrderByDescending(d => d.Title) : deals.OrderBy(d => d.Title),
            "expectedClose" => q.Desc ? deals.OrderByDescending(d => d.ExpectedCloseDate) : deals.OrderBy(d => d.ExpectedCloseDate),
            "stageChanged" => q.Desc ? deals.OrderByDescending(d => d.StageChangedAt) : deals.OrderBy(d => d.StageChangedAt),
            _ => q.Desc ? deals.OrderByDescending(d => d.CreatedAt) : deals.OrderBy(d => d.CreatedAt),
        };
        var total = await deals.CountAsync(ct);
        var rows = await deals.Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<DealSummaryDto>(await DealSummariesAsync(rows, ct), total, q.Page, q.PageSize);
    }

    public async Task<IReadOnlyList<DealSummaryDto>> DealSummariesAsync(IReadOnlyList<CrmDeal> rows, CancellationToken ct)
    {
        var stages = await db.Set<PipelineStage>().AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var companyIds = rows.Where(r => r.CompanyId.HasValue).Select(r => r.CompanyId!.Value).Distinct().ToList();
        var companies = await db.Set<CrmCompany>().AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var contactIds = rows.Where(r => r.PrimaryContactId.HasValue).Select(r => r.PrimaryContactId!.Value).Distinct().ToList();
        var contacts = await db.Set<CrmContact>().AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var users = await UsersAsync(rows.Select(r => r.OwnerUserId), ct);
        return rows.Select(d =>
        {
            var stage = stages.GetValueOrDefault(d.StageId);
            var probability = stage?.WinProbability ?? 0;
            var contact = d.PrimaryContactId is { } pc ? contacts.GetValueOrDefault(pc) : null;
            return new DealSummaryDto(d.Id, d.Title, d.StageId, stage?.Name ?? "—", d.Status, d.Value, d.Currency, probability,
                Money.Round(d.Value * probability / 100m, d.Currency), d.ExpectedCloseDate, d.CompanyId,
                d.CompanyId is { } cid ? companies.GetValueOrDefault(cid) : null, d.PrimaryContactId, contact?.DisplayName, Ref(users, d.OwnerUserId),
                d.Source, d.ServiceSlugs, contact?.Score, d.StageChangedAt, d.CreatedAt, d.ConcurrencyStamp);
        }).ToList();
    }

    public async Task<BoardDto> BoardAsync(DealQuery q, CancellationToken ct)
    {
        var stages = await db.Set<PipelineStage>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Position).ToListAsync(ct);
        var deals = db.Set<CrmDeal>().AsNoTracking();
        if (q.OwnerUserId is { } owner) deals = deals.Where(d => d.OwnerUserId == owner);
        if (!string.IsNullOrWhiteSpace(q.Search)) deals = deals.Where(d => EF.Functions.Like(d.Title, Like(q.Search), "\\"));
        // Open deals, plus deals closed in the last 30 days (so a just-won deal stays visible in the Won column).
        var closedSince = Now.AddDays(-30);
        var rows = await deals.Where(d => d.Status == DealStatus.Open || d.ClosedAt >= closedSince)
            .OrderByDescending(d => d.StageChangedAt).Take(1000).ToListAsync(ct);
        var summaries = await DealSummariesAsync(rows, ct);
        return new BoardDto(stages.Select(s =>
        {
            var inStage = summaries.Where(d => d.StageId == s.Id).ToList();
            return new BoardColumnDto(new StageDto(s.Id, s.Name, s.Position, s.WinProbability, s.Kind, s.IsActive, s.ConcurrencyStamp),
                inStage.Count, inStage.GroupBy(d => d.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(d => d.Value))).OrderBy(c => c.Currency).ToList(),
                inStage);
        }).ToList());
    }

    public async Task<DealDto> GetDealAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Set<CrmDeal>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Deal");
        var stage = await db.Set<PipelineStage>().AsNoTracking().FirstAsync(s => s.Id == d.StageId, ct);
        var companyName = d.CompanyId is { } cid ? await db.Set<CrmCompany>().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var links = await db.Set<CrmDealContact>().AsNoTracking().Where(x => x.DealId == id).ToListAsync(ct);
        var contactIds = links.Select(l => l.ContactId).Append(d.PrimaryContactId ?? Guid.Empty).Distinct().ToList();
        var contacts = await db.Set<CrmContact>().AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var contactDtos = new List<DealContactDto>();
        if (d.PrimaryContactId is { } primary && contacts.TryGetValue(primary, out var pc))
            contactDtos.Add(new DealContactDto(pc.Id, pc.DisplayName, pc.Email, pc.JobTitle, "Primary", true));
        foreach (var l in links.Where(l => l.ContactId != d.PrimaryContactId))
            if (contacts.TryGetValue(l.ContactId, out var c))
                contactDtos.Add(new DealContactDto(c.Id, c.DisplayName, c.Email, c.JobTitle, l.Role, false));
        var proposals = await db.Set<Proposal>().AsNoTracking().Where(p => p.DealId == id).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var proposalIds = proposals.Select(p => p.Id).ToList();
        var versions = await db.Set<ProposalVersion>().AsNoTracking().Where(v => proposalIds.Contains(v.ProposalId))
            .Select(v => new { v.ProposalId, v.VersionNumber, v.Total }).ToListAsync(ct);
        var users = await UsersAsync(new[] { d.OwnerUserId }, ct);
        return new DealDto(d.Id, d.Title, d.StageId, stage.Name, stage.Kind, d.Status, d.Value, d.Currency, stage.WinProbability,
            Money.Round(d.Value * stage.WinProbability / 100m, d.Currency), d.ExpectedCloseDate, d.CompanyId, companyName, d.PrimaryContactId,
            Ref(users, d.OwnerUserId), d.Source, d.SourceDetail, d.BudgetRange, d.ServiceSlugs, ToDto(d.FirstTouch), ToDto(d.LastTouch), d.LostReason,
            d.ClosedAt, d.ClientAccountId, contactDtos,
            proposals.Select(p => new DealProposalDto(p.Id, p.Number, p.Title, p.Status, p.CurrentVersion,
                versions.FirstOrDefault(v => v.ProposalId == p.Id && v.VersionNumber == p.CurrentVersion)?.Total ?? 0, p.Currency, p.SentAt,
                p.AcceptedAt)).ToList(),
            d.StageChangedAt, d.CreatedAt, d.UpdatedAt, d.ConcurrencyStamp);
    }

    public async Task<DealDto> CreateDealAsync(DealRequest r, CancellationToken ct)
    {
        var stage = r.StageId is { } stageId
            ? await db.Set<PipelineStage>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stageId && s.IsActive, ct)
              ?? throw new DomainException("crm.invalid_stage", "Choose an active pipeline stage.")
            : await FirstStageAsync(db, ct);
        if (stage.Kind != StageKind.Open)
            throw new DomainException("crm.invalid_stage", "New deals start in an open stage.");
        var deal = new CrmDeal { StageId = stage.Id, StageChangedAt = Now, FirstTouch = FromDto(r.FirstTouch), LastTouch = FromDto(r.LastTouch) };
        await ApplyAsync(deal, r, ct);
        db.Set<CrmDeal>().Add(deal);
        audit.Record("crm.deal_created", nameof(CrmDeal), deal.Id, after: new { deal.Title, deal.Value, deal.Currency, Stage = stage.Name });
        await db.SaveChangesAsync(ct);
        return await GetDealAsync(deal.Id, ct);
    }

    public async Task<DealDto> UpdateDealAsync(Guid id, DealRequest r, CancellationToken ct)
    {
        var deal = await db.Set<CrmDeal>().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound("Deal");
        RequireStamp(db, deal, r.ConcurrencyStamp);
        var before = new { deal.Title, deal.Value, deal.Currency, deal.OwnerUserId, deal.ExpectedCloseDate };
        await ApplyAsync(deal, r, ct);
        if (r.FirstTouch is not null) deal.FirstTouch = FromDto(r.FirstTouch);
        if (r.LastTouch is not null) deal.LastTouch = FromDto(r.LastTouch);
        audit.Record("crm.deal_updated", nameof(CrmDeal), id, before, new { deal.Title, deal.Value, deal.Currency, deal.OwnerUserId, deal.ExpectedCloseDate });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetDealAsync(id, ct);
    }

    private async Task ApplyAsync(CrmDeal d, DealRequest r, CancellationToken ct)
    {
        await ValidateUserAsync(r.OwnerUserId, "ownerUserId", ct);
        if (r.CompanyId is { } companyId && !await db.Set<CrmCompany>().AnyAsync(c => c.Id == companyId, ct))
            throw new DomainException("crm.invalid_company", "That company doesn't exist.");
        if (r.PrimaryContactId is { } contactId && !await db.Set<CrmContact>().AnyAsync(c => c.Id == contactId, ct))
            throw new DomainException("crm.invalid_contact", "That contact doesn't exist.");
        if (r.ClientAccountId is { } clientId && !await db.Set<Domain.Agency.ClientAccount>().AnyAsync(c => c.Id == clientId, ct))
            throw new DomainException("crm.invalid_client", "That client doesn't exist.");
        var currency = Money.Normalize(r.Currency);
        if (!Money.IsSupported(currency))
            throw new DomainException("crm.currency_unsupported", "Choose a supported currency.",
                errors: new Dictionary<string, string[]> { ["currency"] = new[] { "Choose a supported currency." } });
        if (r.Value < 0 || Money.Round(r.Value, currency) != r.Value)
            throw new DomainException("crm.invalid_value", $"Enter a value of 0 or more with at most {Money.MinorUnitDigits(currency)} decimals.",
                errors: new Dictionary<string, string[]> { ["value"] = new[] { "Enter a valid amount." } });
        var slugs = (r.ServiceSlugs ?? new List<string>()).Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (slugs.Any(s => s.Length > 100 || !s.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-')))
            throw new DomainException("crm.invalid_service", "Service slugs use lower-case letters, digits and dashes.");
        d.Title = r.Title.Trim();
        d.CompanyId = r.CompanyId;
        d.PrimaryContactId = r.PrimaryContactId;
        d.Value = r.Value;
        d.Currency = currency;
        d.ExpectedCloseDate = r.ExpectedCloseDate;
        d.ServiceSlugs = slugs;
        d.OwnerUserId = r.OwnerUserId;
        d.Source = r.Source;
        d.SourceDetail = Trim(r.SourceDetail, 100);
        d.BudgetRange = Trim(r.BudgetRange, 60);
        d.ClientAccountId = r.ClientAccountId ?? d.ClientAccountId;
    }

    /// <summary>
    /// Moves a deal to another stage (kanban drag or keyboard move). Moving to the Lost stage requires a reason; moving to
    /// Won/Lost closes the deal, moving back to an open stage reopens it. Stale stamps answer 409.
    /// </summary>
    public async Task<DealDto> MoveDealAsync(Guid id, MoveDealRequest r, CancellationToken ct)
    {
        var deal = await db.Set<CrmDeal>().FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw DomainException.NotFound("Deal");
        RequireStamp(db, deal, r.ConcurrencyStamp);
        var target = await db.Set<PipelineStage>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.StageId && s.IsActive, ct)
                     ?? throw new DomainException("crm.invalid_stage", "Choose an active pipeline stage.");
        var from = await db.Set<PipelineStage>().AsNoTracking().FirstAsync(s => s.Id == deal.StageId, ct);
        if (from.Id == target.Id) return await GetDealAsync(id, ct);
        var reason = Trim(r.LostReason, 500);
        if (target.Kind == StageKind.Lost && reason is null)
            throw new DomainException("crm.lost_reason_required", "Say why the deal was lost.",
                errors: new Dictionary<string, string[]> { ["lostReason"] = new[] { "Say why the deal was lost." } });
        ApplyStage(deal, target, reason);
        db.Set<CrmActivity>().Add(new CrmActivity
        {
            Type = ActivityType.Note, IsSystem = true, DealId = deal.Id, CompanyId = deal.CompanyId, CreatedByUserId = currentUser.IdOrNull,
            Subject = $"Moved from {from.Name} to {target.Name}", Body = target.Kind == StageKind.Lost ? $"Lost reason: {reason}" : null,
        });
        if (target.Kind == StageKind.Won && deal.PrimaryContactId is { } contactId)
            await db.Set<CrmContact>().Where(c => c.Id == contactId && PreCustomerStages.Contains(c.LifecycleStage))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LifecycleStage, LifecycleStage.Customer), ct);
        audit.Record("crm.deal_moved", nameof(CrmDeal), id, new { Stage = from.Name }, new { Stage = target.Name, deal.Status }, reason);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetDealAsync(id, ct);
    }

    /// <summary>Applies a stage to a tracked deal (status, close date, lost reason).</summary>
    public void ApplyStage(CrmDeal deal, PipelineStage target, string? lostReason)
    {
        deal.StageId = target.Id;
        deal.StageChangedAt = Now;
        deal.Status = target.Kind switch { StageKind.Won => DealStatus.Won, StageKind.Lost => DealStatus.Lost, _ => DealStatus.Open };
        deal.ClosedAt = target.Kind == StageKind.Open ? null : Now;
        deal.LostReason = target.Kind == StageKind.Lost ? lostReason : null;
    }

    public async Task<DealDto> AddDealContactAsync(Guid dealId, DealContactRequest r, CancellationToken ct)
    {
        if (!await db.Set<CrmDeal>().AnyAsync(d => d.Id == dealId, ct)) throw DomainException.NotFound("Deal");
        if (!await db.Set<CrmContact>().AnyAsync(c => c.Id == r.ContactId, ct)) throw DomainException.NotFound("Contact");
        if (!await db.Set<CrmDealContact>().AnyAsync(x => x.DealId == dealId && x.ContactId == r.ContactId, ct))
        {
            db.Set<CrmDealContact>().Add(new CrmDealContact { DealId = dealId, ContactId = r.ContactId!.Value, Role = Trim(r.Role, 80), AddedAt = Now });
            audit.Record("crm.deal_contact_added", nameof(CrmDeal), dealId, after: new { r.ContactId, r.Role });
            await SaveUniqueAsync("crm.duplicate_link", "This contact is already on the deal.", ct);
        }
        return await GetDealAsync(dealId, ct);
    }

    public async Task<DealDto> RemoveDealContactAsync(Guid dealId, Guid contactId, CancellationToken ct)
    {
        if (!await db.Set<CrmDeal>().AnyAsync(d => d.Id == dealId, ct)) throw DomainException.NotFound("Deal");
        var removed = await db.Set<CrmDealContact>().Where(x => x.DealId == dealId && x.ContactId == contactId).ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            audit.Record("crm.deal_contact_removed", nameof(CrmDeal), dealId, before: new { ContactId = contactId });
            await db.SaveChangesAsync(ct);
        }
        return await GetDealAsync(dealId, ct);
    }

    // ================================================================ Activities

    public async Task<PagedResult<ActivityDto>> ListActivitiesAsync(ActivityQuery q, CancellationToken ct)
    {
        var activities = db.Set<CrmActivity>().AsNoTracking();
        if (q.ContactId is { } contact) activities = activities.Where(a => a.ContactId == contact);
        if (q.CompanyId is { } company)
        {
            var dealIds = db.Set<CrmDeal>().Where(d => d.CompanyId == company).Select(d => d.Id);
            var contactIds = db.Set<CrmContact>().Where(c => c.CompanyId == company).Select(c => c.Id);
            activities = activities.Where(a => a.CompanyId == company || (a.DealId != null && dealIds.Contains(a.DealId.Value)) ||
                                               (a.ContactId != null && contactIds.Contains(a.ContactId.Value)));
        }
        if (q.DealId is { } deal) activities = activities.Where(a => a.DealId == deal);
        if (q.Type is { } type) activities = activities.Where(a => a.Type == type);
        if (q.Assignee is "me") activities = activities.Where(a => a.AssigneeUserId == currentUser.Id);
        else if (Guid.TryParse(q.Assignee, out var assignee)) activities = activities.Where(a => a.AssigneeUserId == assignee);
        var now = Now;
        var endOfToday = now.Date.AddDays(1);
        switch (q.Due)
        {
            case "open": activities = activities.Where(a => a.Type == ActivityType.Task && a.CompletedAt == null); break;
            case "overdue": activities = activities.Where(a => a.Type == ActivityType.Task && a.CompletedAt == null && a.DueAt < now); break;
            case "today":
                activities = activities.Where(a => a.CompletedAt == null &&
                    ((a.Type == ActivityType.Task && a.DueAt < endOfToday) ||
                     (a.Type == ActivityType.Meeting && a.OccursAt >= now.Date && a.OccursAt < endOfToday)));
                break;
            case "completed": activities = activities.Where(a => a.CompletedAt != null); break;
        }
        if (!string.IsNullOrWhiteSpace(q.Search)) activities = activities.Where(a => EF.Functions.Like(a.Subject, Like(q.Search), "\\"));
        activities = q.Due is "open" or "overdue" or "today"
            ? activities.OrderBy(a => a.DueAt ?? a.OccursAt)
            : activities.OrderByDescending(a => a.CreatedAt);
        var total = await activities.CountAsync(ct);
        var rows = await activities.Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<ActivityDto>(await ActivityDtosAsync(rows, ct), total, q.Page, q.PageSize);
    }

    public async Task<IReadOnlyList<ActivityDto>> ActivityDtosAsync(IReadOnlyList<CrmActivity> rows, CancellationToken ct)
    {
        var contactIds = rows.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var contacts = await db.Set<CrmContact>().AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);
        var companyIds = rows.Where(r => r.CompanyId.HasValue).Select(r => r.CompanyId!.Value).Distinct().ToList();
        var companies = await db.Set<CrmCompany>().AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var dealIds = rows.Where(r => r.DealId.HasValue).Select(r => r.DealId!.Value).Distinct().ToList();
        var deals = await db.Set<CrmDeal>().AsNoTracking().Where(d => dealIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        var users = await UsersAsync(rows.SelectMany(r => new[] { r.AssigneeUserId, r.CreatedByUserId }), ct);
        var now = Now;
        return rows.Select(a => new ActivityDto(a.Id, a.Type, a.Subject, a.Body, a.ContactId, a.ContactId is { } c ? contacts.GetValueOrDefault(c) : null,
            a.CompanyId, a.CompanyId is { } co ? companies.GetValueOrDefault(co) : null, a.DealId, a.DealId is { } d ? deals.GetValueOrDefault(d) : null,
            a.OccursAt, a.DurationMinutes, a.DueAt, a.RemindAt, Ref(users, a.AssigneeUserId), a.CompletedAt,
            a.Type == ActivityType.Task && a.CompletedAt is null && a.DueAt < now, a.IsSystem, Ref(users, a.CreatedByUserId), a.CreatedAt,
            a.ConcurrencyStamp)).ToList();
    }

    public async Task<ActivityDto> CreateActivityAsync(ActivityRequest r, CancellationToken ct)
    {
        var activity = new CrmActivity { CreatedByUserId = currentUser.IdOrNull };
        await ApplyAsync(activity, r, ct);
        db.Set<CrmActivity>().Add(activity);
        audit.Record("crm.activity_created", nameof(CrmActivity), activity.Id, after: new { activity.Type, activity.Subject, activity.DealId, activity.ContactId });
        await db.SaveChangesAsync(ct);
        return (await ActivityDtosAsync(new[] { activity }, ct))[0];
    }

    public async Task<ActivityDto> UpdateActivityAsync(Guid id, ActivityRequest r, CancellationToken ct)
    {
        var activity = await db.Set<CrmActivity>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("Activity");
        if (activity.IsSystem) throw DomainException.Conflict("crm.system_activity", "System entries can't be edited.");
        RequireStamp(db, activity, r.ConcurrencyStamp);
        var dueChanged = activity.DueAt != r.DueAt;
        var remindChanged = activity.RemindAt != r.RemindAt;
        await ApplyAsync(activity, r, ct);
        if (dueChanged) activity.OverdueNotifiedAt = null;
        if (remindChanged) activity.ReminderSentAt = null;
        audit.Record("crm.activity_updated", nameof(CrmActivity), id, after: new { activity.Type, activity.Subject, activity.DueAt, activity.AssigneeUserId });
        await db.SaveChangesAsync(ct);
        return (await ActivityDtosAsync(new[] { activity }, ct))[0];
    }

    public async Task<ActivityDto> CompleteActivityAsync(Guid id, bool completed, StampOnly r, CancellationToken ct)
    {
        var activity = await db.Set<CrmActivity>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("Activity");
        RequireStamp(db, activity, r.ConcurrencyStamp);
        activity.CompletedAt = completed ? Now : null;
        audit.Record(completed ? "crm.task_completed" : "crm.task_reopened", nameof(CrmActivity), id);
        await db.SaveChangesAsync(ct);
        return (await ActivityDtosAsync(new[] { activity }, ct))[0];
    }

    public async Task DeleteActivityAsync(Guid id, CancellationToken ct)
    {
        var activity = await db.Set<CrmActivity>().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound("Activity");
        if (activity.IsSystem) throw DomainException.Conflict("crm.system_activity", "System entries can't be deleted.");
        db.Remove(activity);
        audit.Record("crm.activity_deleted", nameof(CrmActivity), id, before: new { activity.Type, activity.Subject });
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(CrmActivity a, ActivityRequest r, CancellationToken ct)
    {
        if (r.ContactId is null && r.CompanyId is null && r.DealId is null)
            throw new DomainException("crm.activity_target_required", "Link the activity to a contact, company or deal.");
        if (r.ContactId is { } contact && !await db.Set<CrmContact>().AnyAsync(c => c.Id == contact, ct)) throw DomainException.NotFound("Contact");
        if (r.CompanyId is { } company && !await db.Set<CrmCompany>().AnyAsync(c => c.Id == company, ct)) throw DomainException.NotFound("Company");
        if (r.DealId is { } deal && !await db.Set<CrmDeal>().AnyAsync(d => d.Id == deal, ct)) throw DomainException.NotFound("Deal");
        await ValidateUserAsync(r.AssigneeUserId, "assigneeUserId", ct);
        if (r.Type == ActivityType.Task && r.DueAt is null)
            throw new DomainException("crm.due_required", "Tasks need a due date.",
                errors: new Dictionary<string, string[]> { ["dueAt"] = new[] { "Tasks need a due date." } });
        if (r.Type == ActivityType.Meeting && r.OccursAt is null)
            throw new DomainException("crm.occurs_at_required", "Meetings need a date and time.",
                errors: new Dictionary<string, string[]> { ["occursAt"] = new[] { "Meetings need a date and time." } });
        a.Type = r.Type;
        a.Subject = r.Subject.Trim();
        a.Body = Trim(r.Body, 10000);
        a.ContactId = r.ContactId;
        a.CompanyId = r.CompanyId;
        a.DealId = r.DealId;
        a.OccursAt = r.OccursAt?.ToUniversalTime();
        a.DurationMinutes = r.DurationMinutes;
        a.DueAt = r.Type == ActivityType.Task ? r.DueAt?.ToUniversalTime() : null;
        a.RemindAt = r.RemindAt?.ToUniversalTime();
        a.AssigneeUserId = r.AssigneeUserId ?? (r.Type is ActivityType.Task or ActivityType.Meeting ? currentUser.IdOrNull : null);
    }

    // ================================================================ Saved views

    public async Task<IReadOnlyList<SavedViewDto>> ViewsAsync(string? entity, CancellationToken ct)
    {
        var me = currentUser.Id;
        var views = db.Set<CrmSavedView>().AsNoTracking().Where(v => v.OwnerUserId == me || v.Shared);
        if (!string.IsNullOrWhiteSpace(entity)) views = views.Where(v => v.Entity == entity);
        return (await views.OrderBy(v => v.Name).ToListAsync(ct))
            .Select(v => new SavedViewDto(v.Id, v.Name, v.Entity, ParseFilters(v.FiltersJson), v.Shared, v.OwnerUserId == me, v.CreatedAt)).ToList();
    }

    public async Task<SavedViewDto> CreateViewAsync(SavedViewRequest r, CancellationToken ct)
    {
        if (r.Filters.Count > 20 || r.Filters.Any(kv => kv.Key.Length > 40 || kv.Value.Length > 200 || !kv.Key.All(char.IsAsciiLetterOrDigit)))
            throw new DomainException("crm.invalid_view", "A view can hold up to 20 simple filters.");
        var view = new CrmSavedView
        {
            Name = r.Name.Trim(), Entity = r.Entity, FiltersJson = JsonSerializer.Serialize(r.Filters), Shared = r.Shared, OwnerUserId = currentUser.Id,
        };
        db.Set<CrmSavedView>().Add(view);
        await db.SaveChangesAsync(ct);
        return new SavedViewDto(view.Id, view.Name, view.Entity, r.Filters, view.Shared, true, view.CreatedAt);
    }

    public async Task DeleteViewAsync(Guid id, CancellationToken ct)
    {
        var deleted = await db.Set<CrmSavedView>().Where(v => v.Id == id && v.OwnerUserId == currentUser.Id).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw DomainException.NotFound("View");
    }

    private static IReadOnlyDictionary<string, string> ParseFilters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    // ================================================================ Dashboard

    public async Task<CrmDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var now = Now;
        var stages = await db.Set<PipelineStage>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Position).ToListAsync(ct);
        var open = await db.Set<CrmDeal>().AsNoTracking().Where(d => d.Status == DealStatus.Open)
            .Select(d => new { d.StageId, d.Value, d.Currency }).ToListAsync(ct);
        var probability = stages.ToDictionary(s => s.Id, s => s.WinProbability);
        var pipeline = stages.Where(s => s.Kind == StageKind.Open).Select(s =>
        {
            var inStage = open.Where(d => d.StageId == s.Id).ToList();
            return new StageValueDto(s.Id, s.Name, s.WinProbability, inStage.Count,
                inStage.GroupBy(d => d.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(d => d.Value))).OrderBy(c => c.Currency).ToList(),
                inStage.GroupBy(d => d.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(d => Money.Round(d.Value * s.WinProbability / 100m, g.Key))))
                    .OrderBy(c => c.Currency).ToList());
        }).ToList();
        var openValue = open.GroupBy(d => d.Currency).Select(g => new CurrencyValue(g.Key, g.Sum(d => d.Value))).OrderBy(c => c.Currency).ToList();
        var weighted = open.GroupBy(d => d.Currency).Select(g => new CurrencyValue(g.Key,
            g.Sum(d => Money.Round(d.Value * probability.GetValueOrDefault(d.StageId) / 100m, g.Key)))).OrderBy(c => c.Currency).ToList();

        var since = now.AddDays(-90);
        var closed = await db.Set<CrmDeal>().AsNoTracking().Where(d => d.ClosedAt >= since && d.Status != DealStatus.Open)
            .Select(d => d.Status).ToListAsync(ct);
        var won = closed.Count(s => s == DealStatus.Won);
        var lost = closed.Count(s => s == DealStatus.Lost);
        var sources = await db.Set<CrmDeal>().AsNoTracking().Where(d => d.CreatedAt >= since)
            .GroupBy(d => d.Source).Select(g => new SourceCountDto(g.Key, g.Count(), g.Count(d => d.Status == DealStatus.Won))).ToListAsync(ct);
        var dueToday = await ListActivitiesAsync(new ActivityQuery { Assignee = "me", Due = "today", PageSize = 50 }, ct);
        var me = currentUser.Id;
        var overdue = await db.Set<CrmActivity>().CountAsync(a => a.AssigneeUserId == me && a.Type == ActivityType.Task && a.CompletedAt == null && a.DueAt < now, ct);
        var since30 = now.AddDays(-30);
        var newLeads = await db.Set<CrmContact>().CountAsync(c => c.CreatedAt >= since30, ct);
        return new CrmDashboardDto(pipeline, openValue, weighted, open.Count, won, lost,
            won + lost == 0 ? 0 : Math.Round(won * 100m / (won + lost), 1), sources.OrderByDescending(s => s.Deals).ToList(), dueToday.Items, overdue, newLeads);
    }
}

public sealed class StampOnly
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid? ConcurrencyStamp { get; set; }
}
