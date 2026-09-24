using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>Templated onboarding checklist created for every new client.</summary>
public static class OnboardingChecklistTemplate
{
    public static readonly (string Key, string Title, string Description, string Category, OnboardingOwner Owner)[] Items =
    {
        ("contract-signed", "Contract signed", "The services agreement and statement of work are signed by both sides.", "Commercial", OnboardingOwner.Client),
        ("billing-details", "Billing details confirmed", "Billing contact, address and tax ID are on file so invoices go to the right person.", "Commercial", OnboardingOwner.Client),
        ("kickoff-call", "Kickoff call held", "Goals, KPIs, audiences, approvals and communication preferences agreed in a kickoff call.", "Kickoff", OnboardingOwner.Agency),
        ("ga4-access", "Google Analytics 4 access", "Add the agency as an Editor on your GA4 property (Admin → Property access management).", "Access", OnboardingOwner.Client),
        ("search-console-access", "Search Console access", "Add the agency as a Full user on your Google Search Console property.", "Access", OnboardingOwner.Client),
        ("ad-accounts-access", "Ad accounts access", "Grant access to your Google Ads and Meta Business ad accounts (or approve our partner request).", "Access", OnboardingOwner.Client),
        ("social-accounts-access", "Social accounts access", "Add the agency to your Facebook, Instagram, LinkedIn and TikTok business accounts.", "Access", OnboardingOwner.Client),
        ("brand-assets", "Brand assets uploaded", "Upload logos, fonts, brand guidelines and photography to the brand kit.", "Brand", OnboardingOwner.Client),
        ("brand-kit-reviewed", "Brand kit completed", "Tone of voice, personas, competitors, do's and don'ts and key messages filled in by the team.", "Brand", OnboardingOwner.Agency),
        ("tracking-audit", "Tracking audit", "Conversion tracking, tags and events audited; gaps documented.", "Setup", OnboardingOwner.Agency),
    };
}

public sealed class ClientService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IAuthService auth,
    IPasswordHasher<User> hasher,
    IDatabaseDialect dialect,
    OnboardingTemplateService onboardingTemplate,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string LogoUrl(Guid? fileId) => fileId is { } id ? $"/api/v1/agency/files/{id}" : string.Empty;

    public static string? ClientLogoUrl(Guid clientId, Guid? fileId) => fileId is { } id ? $"/api/v1/client/orgs/{clientId}/files/{id}" : null;

    // ------------------------------------------------------------------ accounts

    public async Task<PagedResult<ClientSummaryDto>> ListAsync(ClientListQuery query, CancellationToken ct)
    {
        var q = (await scope.ApplyAsync(db.Set<ClientAccount>().AsNoTracking(), c => c.Id, ct));
        if (query.Status is { } status) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(query.AccountManager))
        {
            var am = query.AccountManager == "me" ? currentUser.Id : Guid.TryParse(query.AccountManager, out var g) ? g : Guid.Empty;
            q = q.Where(c => c.AccountManagerUserId == am);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(c => EF.Functions.Like(c.Name, p, "\\") || EF.Functions.Like(c.Slug, p, "\\") || (c.Industry != null && EF.Functions.Like(c.Industry, p, "\\")));
        }
        q = query.Sort switch
        {
            "createdAt" => query.Desc ? q.OrderByDescending(c => c.CreatedAt) : q.OrderBy(c => c.CreatedAt),
            "status" => query.Desc ? q.OrderByDescending(c => c.Status).ThenBy(c => c.Name) : q.OrderBy(c => c.Status).ThenBy(c => c.Name),
            _ => q.OrderBy(c => c.Name),
        };
        q = q.ThenByKey(c => c.Id);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip(query.Skip).Take(query.PageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var projects = await db.Set<Project>().AsNoTracking()
            .Where(p => ids.Contains(p.ClientAccountId) && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Planning))
            .GroupBy(p => p.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var members = await db.Set<ClientMember>().AsNoTracking().Where(m => ids.Contains(m.ClientAccountId))
            .GroupBy(m => m.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var people = await PeopleAsync(rows.Select(r => r.AccountManagerUserId), ct);
        var items = rows.Select(c => new ClientSummaryDto(c.Id, c.Name, c.Slug, c.Industry, c.Status, c.Currency, c.CountryCode,
            c.AccountManagerUserId is { } a && people.TryGetValue(a, out var p) ? p : null,
            c.LogoFileId is null ? null : LogoUrl(c.LogoFileId),
            projects.GetValueOrDefault(c.Id), members.GetValueOrDefault(c.Id), c.CreatedAt)).ToList();
        return new PagedResult<ClientSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ClientDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(id, ct: ct);
        var c = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var people = await PeopleAsync(new[] { c.AccountManagerUserId }, ct);
        return ToDetail(c, people);
    }

    public async Task<ClientDetailDto> CreateAsync(CreateClientRequest request, CancellationToken ct)
    {
        var slug = NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
        if (slug.Length < 2) throw DeliveryRules.Invalid("client.invalid_slug", "slug", "Use at least two letters or digits.");
        if (await db.Set<ClientAccount>().AnyAsync(c => c.Slug == slug, ct))
        {
            if (!string.IsNullOrWhiteSpace(request.Slug))
                throw DeliveryRules.Invalid("client.slug_taken", "slug", "Another client already uses this slug.");
            slug = $"{slug}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
        }
        var client = new ClientAccount { Slug = slug, Status = request.Status };
        await ApplyProfileAsync(client, request, ct);
        client.StatusChangedAt = Now;
        db.Set<ClientAccount>().Add(client);

        var sort = 0;
        foreach (var item in await onboardingTemplate.ItemsAsync(ct))
        {
            db.Set<ClientOnboardingItem>().Add(new ClientOnboardingItem
            {
                ClientAccountId = client.Id, Key = item.Key, Title = item.Title, Description = item.Description,
                Category = item.Category, Owner = item.Owner, SortOrder = sort++,
            });
        }
        db.Set<BrandKit>().Add(new BrandKit { ClientAccountId = client.Id });
        if (client.AccountManagerUserId is { } am)
            db.Set<ClientTeamAssignment>().Add(new ClientTeamAssignment
            {
                ClientAccountId = client.Id, UserId = am, ServiceRole = ClientServiceRole.AccountManager, IsPrimary = true,
                AssignedAt = Now, AssignedByUserId = currentUser.Id,
            });
        audit.Record("client.created", nameof(ClientAccount), client.Id, after: Snapshot(client));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DeliveryRules.Invalid("client.slug_taken", "slug", "Another client already uses this slug.");
        }
        return await GetAsync(client.Id, ct);
    }

    public async Task<ClientDetailDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(id, ct: ct);
        var client = await db.Set<ClientAccount>().FirstAsync(c => c.Id == id, ct);
        DeliveryRules.EnsureStamp(client, request.ConcurrencyStamp, db);
        var before = Snapshot(client);
        var previousAm = client.AccountManagerUserId;
        await ApplyProfileAsync(client, request, ct);
        if (client.AccountManagerUserId is { } am && am != previousAm &&
            !await db.Set<ClientTeamAssignment>().AnyAsync(a => a.ClientAccountId == id && a.UserId == am && a.ServiceRole == ClientServiceRole.AccountManager, ct))
        {
            db.Set<ClientTeamAssignment>().Add(new ClientTeamAssignment
            {
                ClientAccountId = id, UserId = am, ServiceRole = ClientServiceRole.AccountManager, IsPrimary = true,
                AssignedAt = Now, AssignedByUserId = currentUser.Id,
            });
        }
        audit.Record("client.updated", nameof(ClientAccount), id, before, Snapshot(client));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ClientDetailDto> ChangeStatusAsync(Guid id, ChangeClientStatusRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(id, ct: ct);
        var status = request.Status!.Value;
        if (!Enum.IsDefined(status)) throw DeliveryRules.Invalid("client.invalid_status", "status", "Choose a status.");
        var reason = request.Reason?.Trim();
        if (status is ClientAccountStatus.Paused or ClientAccountStatus.Churned && string.IsNullOrEmpty(reason))
            throw DeliveryRules.Invalid("client.reason_required", "reason", "Give a reason when pausing or churning a client.");
        var client = await db.Set<ClientAccount>().FirstAsync(c => c.Id == id, ct);
        DeliveryRules.EnsureStamp(client, request.ConcurrencyStamp, db);
        if (client.Status != status)
        {
            var before = new { client.Status, client.StatusReason };
            client.Status = status;
            client.StatusReason = status is ClientAccountStatus.Paused or ClientAccountStatus.Churned ? reason : null;
            client.StatusChangedAt = Now;
            audit.Record("client.status_changed", nameof(ClientAccount), id, before, new { client.Status, client.StatusReason }, reason);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<ClientDetailDto> SetLogoAsync(Guid id, IFormFile? file, DeliveryFileService files, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(id, ct: ct);
        var client = await db.Set<ClientAccount>().FirstAsync(c => c.Id == id, ct);
        var stored = await files.SaveAsync(id, file, ct);
        if (!stored.ContentType.StartsWith("image/", StringComparison.Ordinal))
        {
            files.Discard(stored);
            throw DeliveryRules.Invalid("client.logo_not_image", "file", "The logo must be a PNG, JPEG or WebP image.");
        }
        var before = client.LogoFileId;
        client.LogoFileId = stored.Id;
        audit.Record("client.logo_changed", nameof(ClientAccount), id, new { LogoFileId = before }, new { client.LogoFileId });
        await files.CommitAsync(stored, ct);
        return await GetAsync(id, ct);
    }

    private async Task ApplyProfileAsync(ClientAccount c, ClientProfileRequest r, CancellationToken ct)
    {
        var currency = Money.Normalize(r.Currency);
        if (!Money.IsSupported(currency)) throw DeliveryRules.Invalid("client.invalid_currency", "currency", "Choose a supported currency.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(r.TimeZone.Trim(), out _))
            throw DeliveryRules.Invalid("client.invalid_timezone", "timeZone", "Choose a valid time zone.");
        var country = r.CountryCode.Trim().ToUpperInvariant();
        if (!country.All(char.IsAsciiLetterUpper)) throw DeliveryRules.Invalid("client.invalid_country", "countryCode", "Use a two-letter country code.");
        var website = string.IsNullOrWhiteSpace(r.Website) ? null : r.Website.Trim();
        if (website is not null && !DeliveryRules.IsHttpUrl(website))
            throw DeliveryRules.Invalid("client.invalid_website", "website", "Enter a full http(s) address.");
        if (r.AccountManagerUserId is { } am && !(await StaffDirectory.ValidStaffAsync(db, new[] { am }, ct)).Contains(am))
            throw DeliveryRules.Invalid("client.invalid_account_manager", "accountManagerUserId", "Choose an active staff member.");

        c.Name = r.Name.Trim();
        c.Summary = Blank(r.Summary);
        c.Industry = Blank(r.Industry);
        c.Website = website;
        c.CountryCode = country;
        c.TimeZone = r.TimeZone.Trim();
        c.Currency = currency;
        c.AccountManagerUserId = r.AccountManagerUserId;
        c.BillingContactName = Blank(r.BillingContactName);
        c.BillingEmail = Blank(r.BillingEmail);
        c.BillingAddress = Blank(r.BillingAddress);
        c.TaxId = Blank(r.TaxId);
        c.Notes = Blank(r.Notes);
        c.ApprovalSlaDays = r.ApprovalSlaDays;
        c.AutoApproveAfterDays = r.AutoApproveAfterDays;
    }

    private static object Snapshot(ClientAccount c) => new
    {
        c.Name, c.Slug, c.Industry, c.Website, c.CountryCode, c.TimeZone, c.Currency, c.Status, c.AccountManagerUserId,
        c.BillingContactName, c.BillingEmail, c.ApprovalSlaDays, c.AutoApproveAfterDays,
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string NormalizeSlug(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 100 ? slug[..100].Trim('-') : slug;
    }

    private static ClientDetailDto ToDetail(ClientAccount c, Dictionary<Guid, PersonDto> people) => new(
        c.Id, c.Name, c.Slug, c.Summary, c.Industry, c.Website, c.CountryCode, c.TimeZone, c.Currency, c.Status, c.StatusReason,
        c.StatusChangedAt, c.AccountManagerUserId is { } a && people.TryGetValue(a, out var p) ? p : null, c.LogoFileId,
        c.LogoFileId is null ? null : LogoUrl(c.LogoFileId), c.BillingContactName, c.BillingEmail, c.BillingAddress, c.TaxId,
        c.Notes, c.ApprovalSlaDays, c.AutoApproveAfterDays, c.LastInvoicePaidAt, c.CreatedAt, c.UpdatedAt, c.ConcurrencyStamp);

    internal async Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        return set.Count == 0
            ? new Dictionary<Guid, PersonDto>()
            : await db.Set<User>().AsNoTracking().Where(u => set.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
    }

    // ------------------------------------------------------------------ account team

    public async Task<IReadOnlyList<TeamAssignmentDto>> TeamAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        return await (from a in db.Set<ClientTeamAssignment>().AsNoTracking()
                      join u in db.Set<User>() on a.UserId equals u.Id
                      where a.ClientAccountId == clientId
                      orderby a.ServiceRole, u.DisplayName
                      select new TeamAssignmentDto(a.Id, new PersonDto(u.Id, u.DisplayName, u.Email), a.ServiceRole, a.IsPrimary, a.AssignedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeamAssignmentDto>> AddTeamMemberAsync(Guid clientId, AddTeamMemberRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var userId = request.UserId!.Value;
        var role = request.ServiceRole!.Value;
        if (!Enum.IsDefined(role)) throw DeliveryRules.Invalid("client.invalid_service_role", "serviceRole", "Choose a service role.");
        if (!(await StaffDirectory.ValidStaffAsync(db, new[] { userId }, ct)).Contains(userId))
            throw DeliveryRules.Invalid("client.invalid_team_member", "userId", "Choose an active staff member.");
        if (await db.Set<ClientTeamAssignment>().AnyAsync(a => a.ClientAccountId == clientId && a.UserId == userId && a.ServiceRole == role, ct))
            throw DomainException.Conflict("client.team_member_exists", "This person already has that role on the account team.");
        if (request.IsPrimary)
            await db.Set<ClientTeamAssignment>().Where(a => a.ClientAccountId == clientId && a.ServiceRole == role && a.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsPrimary, false), ct);
        var row = new ClientTeamAssignment
        {
            ClientAccountId = clientId, UserId = userId, ServiceRole = role, IsPrimary = request.IsPrimary, AssignedAt = Now,
            AssignedByUserId = currentUser.Id,
        };
        db.Set<ClientTeamAssignment>().Add(row);
        audit.Record("client.team_member_added", nameof(ClientAccount), clientId, after: new { userId, role, request.IsPrimary });
        await db.SaveChangesAsync(ct);
        return await TeamAsync(clientId, ct);
    }

    public async Task<IReadOnlyList<TeamAssignmentDto>> RemoveTeamMemberAsync(Guid clientId, Guid assignmentId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var row = await db.Set<ClientTeamAssignment>().FirstOrDefaultAsync(a => a.Id == assignmentId && a.ClientAccountId == clientId, ct)
                  ?? throw DomainException.NotFound("TeamAssignment");
        db.Remove(row);
        audit.Record("client.team_member_removed", nameof(ClientAccount), clientId, before: new { row.UserId, row.ServiceRole });
        await db.SaveChangesAsync(ct);
        return await TeamAsync(clientId, ct);
    }

    /// <summary>The account team as shown to client users: names, roles and contact email.</summary>
    public async Task<IReadOnlyList<AccountTeamMemberDto>> AccountTeamAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var am = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == clientId).Select(c => c.AccountManagerUserId).FirstAsync(ct);
        var rows = await (from a in db.Set<ClientTeamAssignment>().AsNoTracking()
                          join u in db.Set<User>() on a.UserId equals u.Id
                          where a.ClientAccountId == clientId && u.Status == UserStatus.Active
                          select new { u.Id, u.DisplayName, u.Email, a.ServiceRole, a.IsPrimary }).ToListAsync(ct);
        if (am is { } amId && rows.All(r => r.Id != amId))
        {
            var user = await db.Set<User>().AsNoTracking().Where(u => u.Id == amId).Select(u => new { u.Id, u.DisplayName, u.Email }).FirstOrDefaultAsync(ct);
            if (user is not null) rows.Add(new { user.Id, user.DisplayName, user.Email, ServiceRole = ClientServiceRole.AccountManager, IsPrimary = true });
        }
        return rows.GroupBy(r => r.Id)
            .Select(g => new AccountTeamMemberDto(g.Key, g.First().DisplayName, g.First().Email, g.Select(x => x.ServiceRole).Distinct().OrderBy(x => x).ToList(),
                g.Key == am, g.Any(x => x.IsPrimary)))
            .OrderByDescending(x => x.IsAccountManager).ThenBy(x => x.Roles.Min()).ThenBy(x => x.DisplayName).ToList();
    }

    // ------------------------------------------------------------------ client users (members)

    /// <summary>Members of the organization. Client users need the Owner duty (staff: clients.view via scope).</summary>
    public async Task<IReadOnlyList<ClientMemberDto>> MembersAsync(Guid clientId, bool asClientOwner, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, asClientOwner ? ClientMemberRole.Owner : ClientMemberRole.Viewer, ct);
        return await (from m in db.Set<ClientMember>().AsNoTracking()
                      join u in db.Set<User>() on m.UserId equals u.Id
                      where m.ClientAccountId == clientId
                      orderby m.Role descending, u.DisplayName
                      select new ClientMemberDto(u.Id, u.DisplayName, u.Email, m.Role, m.AddedAt, u.LastLoginAt, u.LastLoginAt != null))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Invites a client user. A new email gets a Client-role user with an unusable password and a set-password email
    /// (the password-reset flow); an existing client user just gains the membership. Staff accounts cannot be invited.
    /// </summary>
    public async Task<InviteResultDto> InviteAsync(Guid clientId, InviteClientUserRequest request, bool asClientOwner, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, asClientOwner ? ClientMemberRole.Owner : ClientMemberRole.Viewer, ct);
        var role = request.Role!.Value;
        if (!Enum.IsDefined(role)) throw DeliveryRules.Invalid("client.invalid_member_role", "role", "Choose a role.");
        var email = request.Email.Trim();
        var normalized = Normalization.Email(email);
        var client = await db.Set<ClientAccount>().AsNoTracking().FirstAsync(c => c.Id == clientId, ct);

        var created = false;
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (user is null)
            {
                user = new User
                {
                    Email = email,
                    NormalizedEmail = normalized,
                    DisplayName = request.DisplayName.Trim(),
                    CountryCode = client.CountryCode,
                    TimeZone = client.TimeZone,
                    EmailVerifiedAt = Now,
                    ReferralCode = await NewReferralCodeAsync(ct),
                };
                // Unusable random password: the client user chooses their own through the emailed set-password link.
                user.PasswordHash = hasher.HashPassword(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Client, GrantedAt = Now, GrantedByUserId = currentUser.Id });
                db.Set<User>().Add(user);
                created = true;
            }
            else
            {
                // Staff through built-in or custom roles: adding the Client role would mix client.portal with staff permissions.
                if (user.Roles.Any(r => r.Role != Role.Client && r.Role != Role.Participant) ||
                    (await new PermissionDirectory(db).StaffAmongAsync(new[] { user.Id }, ct)).Count > 0)
                    throw DomainException.Conflict("client.invite_staff_account", "This email belongs to an agency staff account and can't be added as a client user.");
                if (user.Status != UserStatus.Active)
                    throw DomainException.Conflict("client.invite_inactive_account", "This account is suspended or deactivated.");
                if (await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == clientId && m.UserId == user.Id, ct))
                    throw DomainException.Conflict("client.member_exists", "This person is already a member of the organization.");
                if (!user.HasRole(Role.Client))
                {
                    user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Client, GrantedAt = Now, GrantedByUserId = currentUser.Id });
                    audit.Record("user.role_granted", nameof(User), user.Id, after: new { Role = Role.Client }, reason: "Invited to a client organization");
                }
            }
            db.Set<ClientMember>().Add(new ClientMember
            {
                ClientAccountId = clientId, UserId = user.Id, Role = role, AddedAt = Now, AddedByUserId = currentUser.Id,
            });
            audit.Record("client.member_invited", nameof(ClientAccount), clientId, after: new { user.Id, user.Email, role, created });
            await notifications.StageAsync(new NotificationRequest(user.Id, DeliveryNotificationTypes.ClientInvited,
                $"You've been added to {client.Name}",
                $"You now have {role} access to {client.Name}'s client portal on Optimize All.",
                DeliveryLinks.ClientHome, created ? null : new[] { NotificationChannel.Email }), ct);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("client.member_exists", "This person is already a member of the organization.");
            }
        }
        // New users get the set-password link (the password-reset flow; valid for one hour, re-sendable via "Forgot password").
        if (created) await auth.ForgotPasswordAsync(user.Email, ct);
        var member = (await MembersAsync(clientId, false, ct)).First(m => m.UserId == user.Id);
        return new InviteResultDto(member, created);
    }

    public async Task<IReadOnlyList<ClientMemberDto>> ChangeMemberRoleAsync(Guid clientId, Guid userId, ChangeMemberRoleRequest request, bool asClientOwner, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, asClientOwner ? ClientMemberRole.Owner : ClientMemberRole.Viewer, ct);
        var role = request.Role!.Value;
        if (!Enum.IsDefined(role)) throw DeliveryRules.Invalid("client.invalid_member_role", "role", "Choose a role.");
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        await dialect.LockRowAsync(db, "client_accounts", clientId, ct);
        var member = await db.Set<ClientMember>().FirstOrDefaultAsync(m => m.ClientAccountId == clientId && m.UserId == userId, ct)
                     ?? throw DomainException.NotFound("ClientMember");
        if (member.Role == ClientMemberRole.Owner && role != ClientMemberRole.Owner)
            await EnsureAnotherOwnerAsync(clientId, userId, ct);
        var before = member.Role;
        member.Role = role;
        audit.Record("client.member_role_changed", nameof(ClientAccount), clientId, new { userId, role = before }, new { userId, role });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await MembersAsync(clientId, asClientOwner, ct);
    }

    public async Task<IReadOnlyList<ClientMemberDto>> RemoveMemberAsync(Guid clientId, Guid userId, bool asClientOwner, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, asClientOwner ? ClientMemberRole.Owner : ClientMemberRole.Viewer, ct);
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        await dialect.LockRowAsync(db, "client_accounts", clientId, ct);
        var member = await db.Set<ClientMember>().FirstOrDefaultAsync(m => m.ClientAccountId == clientId && m.UserId == userId, ct)
                     ?? throw DomainException.NotFound("ClientMember");
        if (member.Role == ClientMemberRole.Owner) await EnsureAnotherOwnerAsync(clientId, userId, ct);
        db.Remove(member);
        audit.Record("client.member_removed", nameof(ClientAccount), clientId, before: new { userId, member.Role });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await MembersAsync(clientId, asClientOwner, ct);
    }

    private async Task EnsureAnotherOwnerAsync(Guid clientId, Guid userId, CancellationToken ct)
    {
        if (!await db.Set<ClientMember>().AnyAsync(m => m.ClientAccountId == clientId && m.UserId != userId && m.Role == ClientMemberRole.Owner, ct))
            throw DomainException.Conflict("client.last_owner", "An organization needs at least one Owner. Make someone else an Owner first.");
    }

    /// <summary>Organizations the calling client user belongs to (org switcher).</summary>
    public async Task<IReadOnlyList<MyOrganizationDto>> MyOrganizationsAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var rows = await (from m in db.Set<ClientMember>().AsNoTracking()
                          join c in db.Set<ClientAccount>() on m.ClientAccountId equals c.Id
                          where m.UserId == me
                          orderby c.Name
                          select new { c.Id, c.Name, c.Slug, c.Status, m.Role, c.LogoFileId, c.Currency, c.TimeZone }).ToListAsync(ct);
        return rows.Select(r => new MyOrganizationDto(r.Id, r.Name, r.Slug, r.Status, r.Role, ClientLogoUrl(r.Id, r.LogoFileId), r.Currency, r.TimeZone)).ToList();
    }

    private async Task<string> NewReferralCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = "CL" + new string(Enumerable.Range(0, 8).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
            if (!await db.Set<User>().AnyAsync(u => u.ReferralCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not allocate a unique referral code.");
    }
}
