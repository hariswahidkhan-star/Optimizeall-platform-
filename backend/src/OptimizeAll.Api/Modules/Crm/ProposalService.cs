using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>
/// Proposal builder: drafts, versioning (editing a sent proposal creates a new version), totals (server-side preview),
/// sending (unguessable share link + email), withdrawal and the public view (with view tracking).
/// </summary>
public sealed class ProposalService(
    AppDbContext db,
    IDatabaseDialect dialect,
    ICurrentUser currentUser,
    IAuditLogger audit,
    INotificationService notifications,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    LineBuilder lineBuilder,
    DocumentNumberService numbers,
    BillingSettingsService billingSettings,
    PublicLinkTokens tokens,
    TimeProvider clock,
    ILogger<ProposalService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => BillingDates.Today(clock);

    /// <summary>Status as the client experiences it: an unanswered proposal past its validity date is Expired.</summary>
    public static ProposalStatus EffectiveStatus(Proposal p, DateOnly validUntil, DateOnly today) =>
        Proposal.IsAwaitingClient(p.Status) && validUntil < today ? ProposalStatus.Expired : p.Status;

    // ------------------------------------------------------------------ Queries

    public async Task<PagedResult<ProposalSummaryDto>> ListAsync(ProposalQuery q, CancellationToken ct)
    {
        var proposals = db.Set<Proposal>().AsNoTracking();
        if (q.DealId is { } deal) proposals = proposals.Where(p => p.DealId == deal);
        if (q.ClientAccountId is { } client) proposals = proposals.Where(p => p.ClientAccountId == client);
        if (q.Status is { } status) proposals = proposals.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = PagingExtensions.LikePattern(q.Search);
            proposals = proposals.Where(p => EF.Functions.Like(p.Title, pattern, "\\") || EF.Functions.Like(p.Number, pattern, "\\"));
        }
        var total = await proposals.CountAsync(ct);
        var rows = await proposals.OrderByDescending(p => p.CreatedAt).Skip(q.Skip).Take(q.PageSize).ToListAsync(ct);
        return new PagedResult<ProposalSummaryDto>(await SummariesAsync(rows, ct), total, q.Page, q.PageSize);
    }

    public async Task<IReadOnlyList<ProposalSummaryDto>> SummariesAsync(IReadOnlyList<Proposal> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var versions = await db.Set<ProposalVersion>().AsNoTracking().Where(v => ids.Contains(v.ProposalId))
            .Select(v => new { v.ProposalId, v.VersionNumber, v.Total, v.MonthlyRecurringValue, v.ValidUntil }).ToListAsync(ct);
        var dealIds = rows.Where(r => r.DealId.HasValue).Select(r => r.DealId!.Value).ToList();
        var deals = await db.Set<CrmDeal>().AsNoTracking().Where(d => dealIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        var clientIds = rows.Where(r => r.ClientAccountId.HasValue).Select(r => r.ClientAccountId!.Value).ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var companyIds = rows.Where(r => r.CompanyId.HasValue).Select(r => r.CompanyId!.Value).ToList();
        var companies = await db.Set<CrmCompany>().AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var today = Today;
        return rows.Select(p =>
        {
            var v = versions.First(x => x.ProposalId == p.Id && x.VersionNumber == p.CurrentVersion);
            return new ProposalSummaryDto(p.Id, p.Number, p.Title, EffectiveStatus(p, v.ValidUntil, today), p.DealId,
                p.DealId is { } d ? deals.GetValueOrDefault(d) : null, p.ClientAccountId, p.ClientAccountId is { } c ? clients.GetValueOrDefault(c) : null,
                p.CompanyId is { } co ? companies.GetValueOrDefault(co) : null, p.Currency, v.Total, v.MonthlyRecurringValue, p.CurrentVersion,
                v.ValidUntil, p.SentAt, p.ViewCount, p.AcceptedAt, p.CreatedAt);
        }).ToList();
    }

    public async Task<ProposalDto> GetAsync(Guid id, CancellationToken ct, int? versionNumber = null)
    {
        var p = await db.Set<Proposal>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        var versions = await db.Set<ProposalVersion>().AsNoTracking().Where(v => v.ProposalId == id).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var wanted = versionNumber ?? p.CurrentVersion;
        var current = versions.FirstOrDefault(v => v.VersionNumber == wanted) ?? throw DomainException.NotFound("ProposalVersion");
        var version = await VersionDtoAsync(current, ct);
        var dealTitle = p.DealId is { } d ? await db.Set<CrmDeal>().Where(x => x.Id == d).Select(x => x.Title).FirstOrDefaultAsync(ct) : null;
        var clientName = p.ClientAccountId is { } c ? await db.Set<ClientAccount>().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var companyName = p.CompanyId is { } co ? await db.Set<CrmCompany>().Where(x => x.Id == co).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var contact = p.ContactId is { } ci ? await db.Set<CrmContact>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == ci, ct) : null;
        var raw = tokens.Reveal(p.ShareTokenProtected);
        var latest = versions.First(v => v.VersionNumber == p.CurrentVersion);
        return new ProposalDto(p.Id, p.Number, p.Title, EffectiveStatus(p, latest.ValidUntil, Today), p.DealId, dealTitle, p.ClientAccountId, clientName,
            p.CompanyId, companyName, p.ContactId, contact?.DisplayName, p.Currency, p.CurrentVersion, p.SentVersion, p.RecipientName, p.RecipientEmail,
            p.InvoiceOnAcceptance, raw is null ? null : ShareUrl(raw), p.SentAt, p.ViewCount, p.FirstViewedAt, p.LastViewedAt, p.AcceptedAt,
            p.AcceptedVersion, p.SignerName, p.SignerTitle, p.SignerEmail, p.DeclinedAt, p.DeclineReason, version,
            versions.Select(v => new ProposalVersionSummaryDto(v.VersionNumber, v.Total, v.MonthlyRecurringValue, v.Currency, v.CreatedAt, v.SentAt, v.Locked))
                .ToList(),
            await db.Set<Contract>().AsNoTracking().Where(x => x.ProposalId == id).Select(x => x.Id).ToListAsync(ct),
            await db.Set<Invoice>().AsNoTracking().Where(x => x.ProposalId == id).Select(x => x.Id).ToListAsync(ct),
            p.CreatedAt, p.ConcurrencyStamp);
    }

    public string ShareUrl(string raw) => emailOptions.Value.AppBaseUrl.TrimEnd('/') + BillingLinks.PublicProposal(raw);

    public async Task<ProposalVersionDto> VersionDtoAsync(ProposalVersion v, CancellationToken ct)
    {
        var lines = await db.Set<ProposalLine>().AsNoTracking().Where(l => l.ProposalVersionId == v.Id).OrderBy(l => l.Position).ToListAsync(ct);
        var inputs = lines.Select(l => l.ToInput(l.Recurrence)).ToList();
        return new ProposalVersionDto(v.VersionNumber, v.Title, v.Currency, v.ValidUntil, v.ExecutiveSummary, v.Goals, v.Scope, v.Deliverables,
            v.Timeline, v.Terms, lines.Select(LineBuilder.ToDto).ToList(), LineBuilder.ToDto(Pricing.Totals(inputs, v.Currency)),
            LineBuilder.ToDto(Pricing.Recurring(inputs, v.Currency)), v.CreatedAt, v.SentAt, v.Locked);
    }

    // ------------------------------------------------------------------ Create & edit (versioning)

    public async Task<ProposalDto> CreateAsync(ProposalRequest r, CancellationToken ct)
    {
        var links = await ResolveLinksAsync(r, ct);
        var settings = await billingSettings.GetAsync(ct);
        var currency = LineBuilder.NormalizeCurrency(r.Currency ?? links.DefaultCurrency ?? settings.DefaultCurrency);
        ValidateValidity(r.ValidUntil!.Value);
        var built = await lineBuilder.BuildAsync(r.Lines, currency, () => new ProposalLine(), ct, allowRecurrence: true);
        await numbers.EnsureAsync(db, DocumentNumberService.ProposalSeries, ct);
        Proposal proposal;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            proposal = new Proposal
            {
                Number = await numbers.NextAsync(db, DocumentNumberService.ProposalSeries, settings.ProposalPrefix, settings.NumberPadding, ct),
                Title = r.Title.Trim(),
                DealId = r.DealId,
                CompanyId = links.CompanyId,
                ContactId = links.ContactId,
                ClientAccountId = links.ClientAccountId,
                Currency = currency,
                CurrentVersion = 1,
                RecipientName = CrmService.Trim(r.RecipientName, 150) ?? links.ContactName,
                RecipientEmail = CrmService.Trim(r.RecipientEmail, 254) ?? links.ContactEmail,
                InvoiceOnAcceptance = r.InvoiceOnAcceptance,
                CreatedByUserId = currentUser.IdOrNull,
            };
            proposal.Versions.Add(NewVersion(proposal, 1, r, currency, built));
            db.Set<Proposal>().Add(proposal);
            audit.Record("crm.proposal_created", nameof(Proposal), proposal.Id,
                after: new { proposal.Number, proposal.Title, proposal.DealId, Total = built.Totals.Total, proposal.Currency });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await GetAsync(proposal.Id, ct);
    }

    /// <summary>
    /// Edits the proposal. A draft version that was never sent is edited in place; once the current version has been sent
    /// (or declined/expired), the edit becomes a new version and the proposal returns to Draft until it is sent again.
    /// Accepted and withdrawn proposals are locked.
    /// </summary>
    public async Task<ProposalDto> UpdateAsync(Guid id, ProposalRequest r, CancellationToken ct)
    {
        var proposal = await db.Set<Proposal>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        CrmService.RequireStamp(db, proposal, r.ConcurrencyStamp);
        if (proposal.Status is ProposalStatus.Accepted or ProposalStatus.Withdrawn)
            throw DomainException.Conflict("proposal.locked", $"A {proposal.Status.ToString().ToLowerInvariant()} proposal can't be edited.");
        var links = await ResolveLinksAsync(r, ct);
        var current = await db.Set<ProposalVersion>().FirstAsync(v => v.ProposalId == id && v.VersionNumber == proposal.CurrentVersion, ct);
        var currency = LineBuilder.NormalizeCurrency(r.Currency ?? proposal.Currency);
        ValidateValidity(r.ValidUntil!.Value);
        var built = await lineBuilder.BuildAsync(r.Lines, currency, () => new ProposalLine(), ct, allowRecurrence: true);
        var before = new { proposal.CurrentVersion, proposal.Status, Total = current.Total };
        var newVersion = current.SentAt is not null || current.Locked;
        if (newVersion)
        {
            proposal.CurrentVersion += 1;
            var version = NewVersion(proposal, proposal.CurrentVersion, r, currency, built);
            version.ProposalId = proposal.Id;
            db.Set<ProposalVersion>().Add(version);
            // The client keeps seeing the sent version as "being revised" until the new one is sent.
            proposal.Status = ProposalStatus.Draft;
        }
        else
        {
            var oldLines = await db.Set<ProposalLine>().Where(l => l.ProposalVersionId == current.Id).ToListAsync(ct);
            db.RemoveRange(oldLines);
            foreach (var line in built.Lines)
            {
                line.ProposalVersionId = current.Id;
                db.Set<ProposalLine>().Add(line);
            }
            Fill(current, r, currency, built);
        }
        proposal.Title = r.Title.Trim();
        proposal.Currency = currency;
        proposal.DealId = r.DealId;
        proposal.CompanyId = links.CompanyId;
        proposal.ContactId = links.ContactId;
        proposal.ClientAccountId = links.ClientAccountId ?? proposal.ClientAccountId;
        proposal.RecipientName = CrmService.Trim(r.RecipientName, 150) ?? proposal.RecipientName;
        proposal.RecipientEmail = CrmService.Trim(r.RecipientEmail, 254) ?? proposal.RecipientEmail;
        proposal.InvoiceOnAcceptance = r.InvoiceOnAcceptance;
        audit.Record(newVersion ? "crm.proposal_revised" : "crm.proposal_updated", nameof(Proposal), id, before,
            new { proposal.CurrentVersion, proposal.Status, Total = built.Totals.Total });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    private ProposalVersion NewVersion(Proposal p, int number, ProposalRequest r, string currency, LineBuilder.Built<ProposalLine> built)
    {
        var v = new ProposalVersion { ProposalId = p.Id, VersionNumber = number, CreatedAt = Now, CreatedByUserId = currentUser.IdOrNull, Lines = built.Lines };
        Fill(v, r, currency, built);
        return v;
    }

    private static void Fill(ProposalVersion v, ProposalRequest r, string currency, LineBuilder.Built<ProposalLine> built)
    {
        v.Title = r.Title.Trim();
        v.Currency = currency;
        v.ValidUntil = r.ValidUntil!.Value;
        v.ExecutiveSummary = CrmService.Trim(r.ExecutiveSummary, 20000);
        v.Goals = CrmService.Trim(r.Goals, 20000);
        v.Scope = CrmService.Trim(r.Scope, 20000);
        v.Deliverables = CrmService.Trim(r.Deliverables, 20000);
        v.Timeline = CrmService.Trim(r.Timeline, 20000);
        v.Terms = CrmService.Trim(r.Terms, 20000);
        v.GrossTotal = built.Totals.GrossTotal;
        v.DiscountTotal = built.Totals.DiscountTotal;
        v.Subtotal = built.Totals.Subtotal;
        v.TaxTotal = built.Totals.TaxTotal;
        v.Total = built.Totals.Total;
        v.OneTimeTotal = built.Recurring.OneTimeTotal;
        v.MonthlyRecurringValue = built.Recurring.MonthlyRecurringValue;
        v.FirstYearValue = built.Recurring.FirstYearValue;
    }

    private void ValidateValidity(DateOnly validUntil)
    {
        if (validUntil < Today)
            throw new DomainException("proposal.invalid_validity", "The validity date can't be in the past.",
                errors: new Dictionary<string, string[]> { ["validUntil"] = new[] { "Choose today or a later date." } });
        if (validUntil > Today.AddYears(1))
            throw new DomainException("proposal.invalid_validity", "A proposal can be valid for at most one year.",
                errors: new Dictionary<string, string[]> { ["validUntil"] = new[] { "Choose a date within a year." } });
    }

    private sealed record Links(Guid? CompanyId, Guid? ContactId, Guid? ClientAccountId, string? DefaultCurrency, string? ContactName, string? ContactEmail);

    private async Task<Links> ResolveLinksAsync(ProposalRequest r, CancellationToken ct)
    {
        CrmDeal? deal = null;
        if (r.DealId is { } dealId)
            deal = await db.Set<CrmDeal>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealId, ct)
                   ?? throw new DomainException("proposal.invalid_deal", "That deal doesn't exist.");
        var clientId = r.ClientAccountId ?? deal?.ClientAccountId;
        ClientAccount? client = null;
        if (clientId is { } cid)
            client = await db.Set<ClientAccount>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct)
                     ?? throw new DomainException("proposal.invalid_client", "That client doesn't exist.");
        var companyId = r.CompanyId ?? deal?.CompanyId;
        if (companyId is { } co && !await db.Set<CrmCompany>().AnyAsync(c => c.Id == co, ct))
            throw new DomainException("proposal.invalid_company", "That company doesn't exist.");
        var contactId = r.ContactId ?? deal?.PrimaryContactId;
        var contact = contactId is { } ci ? await db.Set<CrmContact>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == ci, ct) : null;
        if (contactId is not null && contact is null) throw new DomainException("proposal.invalid_contact", "That contact doesn't exist.");
        return new Links(companyId, contactId, clientId, client?.Currency ?? deal?.Currency, contact?.DisplayName, contact?.Email);
    }

    // ------------------------------------------------------------------ Send & withdraw

    public async Task<SendProposalResponse> SendAsync(Guid id, SendProposalRequest r, CancellationToken ct)
    {
        var proposal = await db.Set<Proposal>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        CrmService.RequireStamp(db, proposal, r.ConcurrencyStamp);
        if (proposal.Status is not (ProposalStatus.Draft or ProposalStatus.Sent or ProposalStatus.Viewed))
            throw DomainException.Conflict("proposal.not_sendable", $"A {proposal.Status.ToString().ToLowerInvariant()} proposal can't be sent.");
        var version = await db.Set<ProposalVersion>().FirstAsync(v => v.ProposalId == id && v.VersionNumber == proposal.CurrentVersion, ct);
        if (version.ValidUntil < Today)
            throw DomainException.Conflict("proposal.expired", "This proposal's validity date has passed. Edit it to set a new date, then send it.");
        if (r.Email && string.IsNullOrWhiteSpace(proposal.RecipientEmail))
            throw new DomainException("proposal.recipient_required", "Add the recipient's email before sending, or publish the link without emailing.");

        string raw;
        if (tokens.Reveal(proposal.ShareTokenProtected) is { } existing) raw = existing;
        else
        {
            var created = tokens.Create();
            raw = created.Raw;
            proposal.ShareTokenHash = created.Hash;
            proposal.ShareTokenProtected = created.Protected;
        }
        var firstSendOfVersion = version.SentAt is null;
        version.SentAt ??= Now;
        proposal.SentVersion = proposal.CurrentVersion;
        if (proposal.Status == ProposalStatus.Draft) proposal.Status = ProposalStatus.Sent;
        proposal.SentAt = Now;
        proposal.SentByUserId = currentUser.IdOrNull;
        var url = ShareUrl(raw);
        var agency = (await billingSettings.GetAsync(ct)).CompanyName;

        // Client members (Billing/Owner) of an existing client also get it in their portal.
        if (proposal.ClientAccountId is { } clientId)
        {
            var members = await db.Set<ClientMember>().AsNoTracking()
                .Where(m => m.ClientAccountId == clientId && (m.Role == ClientMemberRole.Billing || m.Role == ClientMemberRole.Owner))
                .Select(m => m.UserId).ToListAsync(ct);
            foreach (var userId in members)
                await notifications.StageAsync(new NotificationRequest(userId, BillingNotificationTypes.ProposalReceived,
                    $"New proposal: {proposal.Title}", $"{agency} sent you proposal {proposal.Number} (version {proposal.CurrentVersion}) to review.",
                    BillingLinks.ClientProposal(proposal.Id)), ct);
        }
        if (proposal.DealId is { } dealId)
            db.Set<CrmActivity>().Add(new CrmActivity
            {
                Type = ActivityType.Email, IsSystem = true, DealId = dealId, ContactId = proposal.ContactId, CreatedByUserId = currentUser.IdOrNull,
                Subject = $"Proposal {proposal.Number} v{proposal.CurrentVersion} sent", Body = r.Email ? $"Emailed to {proposal.RecipientEmail}" : "Link shared",
                OccursAt = Now,
            });
        audit.Record("crm.proposal_sent", nameof(Proposal), id, after: new { proposal.Number, Version = proposal.CurrentVersion, r.Email, firstSendOfVersion });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var emailed = false;
        if (r.Email)
        {
            var body = $"Hello {proposal.RecipientName ?? "there"},\n\n" +
                       (string.IsNullOrWhiteSpace(r.Message) ? $"Please find our proposal \"{proposal.Title}\" below." : r.Message.Trim()) +
                       $"\n\nReview and accept the proposal online: {url}\n\nThis proposal is valid until {version.ValidUntil:d MMM yyyy}.\n\n— {agency}";
            var result = await email.SendAsync(new EmailMessage(proposal.RecipientEmail!, proposal.RecipientName ?? proposal.RecipientEmail!,
                $"Proposal {proposal.Number}: {proposal.Title}", body), ct);
            emailed = result.Success;
            if (!result.Success) logger.LogWarning("Proposal {ProposalId} email failed: {Error}", id, result.Error);
        }
        return new SendProposalResponse(await GetAsync(id, ct), url, emailed);
    }

    public async Task<ProposalDto> WithdrawAsync(Guid id, WithdrawProposalRequest r, CancellationToken ct)
    {
        var proposal = await db.Set<Proposal>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        CrmService.RequireStamp(db, proposal, r.ConcurrencyStamp);
        if (proposal.Status is ProposalStatus.Accepted or ProposalStatus.Withdrawn)
            throw DomainException.Conflict("proposal.locked", $"A {proposal.Status.ToString().ToLowerInvariant()} proposal can't be withdrawn.");
        var before = proposal.Status;
        proposal.Status = ProposalStatus.Withdrawn;
        audit.Record("crm.proposal_withdrawn", nameof(Proposal), id, new { Status = before }, new { proposal.Status }, CrmService.Trim(r.Reason, 1000));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(id, ct);
    }

    // ------------------------------------------------------------------ Delete & duplicate

    /// <summary>
    /// Deletes a proposal that was never sent (nothing reached the client, so there is nothing to keep). Anything that was
    /// sent is part of the sales record: withdraw it instead.
    /// </summary>
    public async Task DeleteDraftAsync(Guid id, Guid? stamp, CancellationToken ct)
    {
        var proposal = await db.Set<Proposal>().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        CrmService.RequireStamp(db, proposal, stamp);
        if (proposal.SentVersion is not null || proposal.Status != ProposalStatus.Draft)
            throw DomainException.Conflict("proposal.not_deletable",
                "Only a proposal that was never sent can be deleted. Withdraw it instead to disable the client's link.");
        db.Remove(proposal);
        audit.Record("crm.proposal_deleted", nameof(Proposal), id, before: new { proposal.Number, proposal.Title, proposal.DealId });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Copies the proposal's latest version into a new draft (for example to re-issue a withdrawn or declined proposal).</summary>
    public async Task<ProposalDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await db.Set<Proposal>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Proposal");
        var version = await db.Set<ProposalVersion>().AsNoTracking().FirstAsync(v => v.ProposalId == id && v.VersionNumber == source.CurrentVersion, ct);
        var lines = await db.Set<ProposalLine>().AsNoTracking().Where(l => l.ProposalVersionId == version.Id).OrderBy(l => l.Position).ToListAsync(ct);
        var title = "Copy of " + source.Title;
        var request = new ProposalRequest
        {
            Title = title.Length > 200 ? title[..200] : title,
            DealId = source.DealId,
            ClientAccountId = source.ClientAccountId,
            CompanyId = source.CompanyId,
            ContactId = source.ContactId,
            Currency = source.Currency,
            ValidUntil = version.ValidUntil >= Today.AddDays(7) ? version.ValidUntil : Today.AddDays(30),
            ExecutiveSummary = version.ExecutiveSummary,
            Goals = version.Goals,
            Scope = version.Scope,
            Deliverables = version.Deliverables,
            Timeline = version.Timeline,
            Terms = version.Terms,
            RecipientName = source.RecipientName,
            RecipientEmail = source.RecipientEmail,
            InvoiceOnAcceptance = source.InvoiceOnAcceptance,
            Lines = lines.Select(l => new PriceLineRequest
            {
                Description = l.Description, ServiceSlug = l.ServiceSlug, PackageSlug = l.PackageSlug, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                DiscountType = l.DiscountType, DiscountValue = l.DiscountValue, TaxRateId = l.TaxRateId, Recurrence = l.Recurrence,
            }).ToList(),
        };
        var copy = await CreateAsync(request, ct);
        audit.Record("crm.proposal_duplicated", nameof(Proposal), copy.Id, after: new { From = source.Number, copy.Number });
        await db.SaveChangesAsync(ct);
        return copy;
    }

    // ------------------------------------------------------------------ Public view

    public async Task<Proposal> FindByTokenAsync(string token, CancellationToken ct)
    {
        if (!PublicLinkTokens.IsWellFormed(token)) throw DomainException.NotFound("Proposal");
        var hash = PublicLinkTokens.Hash(token);
        var proposal = await db.Set<Proposal>().AsNoTracking().FirstOrDefaultAsync(p => p.ShareTokenHash == hash, ct);
        if (proposal is null || proposal.SentVersion is null || proposal.Status == ProposalStatus.Withdrawn) throw DomainException.NotFound("Proposal");
        return proposal;
    }

    public async Task<PublicProposalDto> PublicViewAsync(Proposal p, CancellationToken ct)
    {
        var sentVersion = p.AcceptedVersion ?? p.SentVersion ?? p.CurrentVersion;
        var version = await db.Set<ProposalVersion>().AsNoTracking().FirstAsync(v => v.ProposalId == p.Id && v.VersionNumber == sentVersion, ct);
        var preparedFor = p.ClientAccountId is { } c
            ? await db.Set<ClientAccount>().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : p.CompanyId is { } co ? await db.Set<CrmCompany>().Where(x => x.Id == co).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var status = EffectiveStatus(p, version.ValidUntil, Today);
        var beingRevised = p.Status == ProposalStatus.Draft;
        return new PublicProposalDto(p.Number, version.Title, beingRevised ? ProposalStatus.Sent : status, (await billingSettings.GetAsync(ct)).CompanyName,
            preparedFor, p.RecipientName, await VersionDtoAsync(version, ct),
            CanRespond: Proposal.IsAwaitingClient(p.Status) && status != ProposalStatus.Expired, Expired: status == ProposalStatus.Expired,
            BeingRevised: beingRevised, p.AcceptedAt, p.SignerName, p.SignerTitle, p.DeclinedAt);
    }

    /// <summary>Counts a view of the public page (atomic; Sent → Viewed on the first view) and tells the owner once.</summary>
    public async Task RecordViewAsync(Proposal p, CancellationToken ct)
    {
        if (!Proposal.IsAwaitingClient(p.Status)) return;
        var now = Now;
        await db.Set<Proposal>().Where(x => x.Id == p.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ViewCount, x => x.ViewCount + 1).SetProperty(x => x.LastViewedAt, now), ct);
        var first = await db.Set<Proposal>().Where(x => x.Id == p.Id && x.FirstViewedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FirstViewedAt, now), ct);
        await db.Set<Proposal>().Where(x => x.Id == p.Id && x.Status == ProposalStatus.Sent)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ProposalStatus.Viewed), ct);
        if (first == 1)
        {
            var ownerId = p.DealId is { } d ? await db.Set<CrmDeal>().Where(x => x.Id == d).Select(x => x.OwnerUserId).FirstOrDefaultAsync(ct) : null;
            if ((ownerId ?? p.SentByUserId ?? p.CreatedByUserId) is { } notify)
            {
                await notifications.StageAsync(new NotificationRequest(notify, BillingNotificationTypes.ProposalViewed,
                    $"Proposal {p.Number} was opened", $"{p.RecipientName ?? "The client"} opened \"{p.Title}\" for the first time.",
                    BillingLinks.AgencyProposal(p.Id)), ct);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
