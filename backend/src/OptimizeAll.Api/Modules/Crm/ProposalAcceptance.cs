using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Auth;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>Who accepted/declined and from where (the IP is only stored as a keyed hash).</summary>
public sealed record SignerContext(string? IpAddress, string? UserAgent, Guid? UserId);

/// <summary>
/// Accepting and declining proposals (public link or client portal). Acceptance is exactly-once: the proposal row is locked
/// and its status moves Sent/Viewed → Accepted only if the client accepted the version currently on offer and it hasn't
/// expired; everything the acceptance creates (client account + invitation, contracts from recurring lines, the first
/// invoice, the deal moving to Won) happens in that same transaction and carries unique keys.
/// </summary>
public sealed class ProposalAcceptanceService(
    AppDbContext db,
    IDatabaseDialect dialect,
    IAuditLogger audit,
    INotificationService notifications,
    IEventPublisher events,
    IPrivacyHasher privacyHasher,
    IPasswordHasher<User> passwordHasher,
    IAuthService auth,
    CrmService crm,
    ProposalService proposals,
    InvoiceService invoices,
    DocumentNumberService numbers,
    BillingSettingsService billingSettings,
    TimeProvider clock,
    ILogger<ProposalAcceptanceService> logger)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AcceptProposalResponse> AcceptAsync(Guid proposalId, AcceptProposalRequest r, SignerContext signer, CancellationToken ct)
    {
        if (!r.AgreeToTerms)
            throw new DomainException("proposal.terms_required", "Please confirm that you agree to the terms.",
                errors: new Dictionary<string, string[]> { ["agreeToTerms"] = new[] { "Please confirm that you agree to the terms." } });
        var fullName = r.FullName.Trim();
        var title = r.Title.Trim();
        if (fullName.Length < 2) throw new DomainException("proposal.name_required", "Type your full name.",
            errors: new Dictionary<string, string[]> { ["fullName"] = new[] { "Type your full name." } });

        var settings = await billingSettings.GetAsync(ct);
        await numbers.EnsureAsync(db, DocumentNumberService.ContractSeries, ct);
        await numbers.EnsureAsync(db, DocumentNumberService.InvoiceSeries, ct);
        var today = BillingDates.Today(clock);

        Proposal proposal;
        ClientAccount? createdClient = null;
        string? inviteEmail = null;
        var contractsCreated = 0;
        Invoice? invoice = null;
        Func<CancellationToken, Task>? afterCommit = null;
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "proposals", proposalId, ct)) throw DomainException.NotFound("Proposal");
            proposal = await db.Set<Proposal>().FirstAsync(p => p.Id == proposalId, ct);
            var version = await EnsureRespondableAsync(proposal, r.Version, today, ct);

            // ---- Lock the accepted version and record the signature
            proposal.Status = ProposalStatus.Accepted;
            proposal.AcceptedAt = Now;
            proposal.AcceptedVersion = version.VersionNumber;
            proposal.SignerName = fullName;
            proposal.SignerTitle = title;
            proposal.SignerEmail = CrmService.Trim(r.Email, 254) ?? proposal.RecipientEmail;
            proposal.SignerIpHash = privacyHasher.Hash(signer.IpAddress);
            proposal.SignerUserAgent = CrmService.Trim(signer.UserAgent, 500);
            proposal.AcceptedByUserId = signer.UserId;
            version.Locked = true;
            var lines = await db.Set<ProposalLine>().AsNoTracking().Where(l => l.ProposalVersionId == version.Id).OrderBy(l => l.Position).ToListAsync(ct);

            // ---- Deal → Won
            CrmDeal? deal = proposal.DealId is { } dealId ? await db.Set<CrmDeal>().FirstOrDefaultAsync(d => d.Id == dealId, ct) : null;
            if (deal is not null && deal.Status != DealStatus.Won)
            {
                var won = await db.Set<PipelineStage>().AsNoTracking().FirstOrDefaultAsync(s => s.Kind == StageKind.Won, ct);
                if (won is not null)
                {
                    var fromStage = deal.StageId;
                    crm.ApplyStage(deal, won, null);
                    db.Set<CrmActivity>().Add(new CrmActivity
                    {
                        Type = ActivityType.Note, IsSystem = true, DealId = deal.Id, CompanyId = deal.CompanyId,
                        Subject = $"Won — proposal {proposal.Number} v{version.VersionNumber} accepted by {fullName} ({title})",
                    });
                    audit.RecordSystem("crm.deal_won", nameof(CrmDeal), deal.Id, new { FromStage = fromStage, ProposalId = proposal.Id });
                }
            }

            // ---- Client account (+ owner invitation for the signer) when the deal has none yet
            var clientId = proposal.ClientAccountId ?? deal?.ClientAccountId;
            if (clientId is null)
            {
                var company = proposal.CompanyId is { } coId ? await db.Set<CrmCompany>().FirstOrDefaultAsync(c => c.Id == coId, ct) : null;
                var name = company?.Name ?? CrmService.Trim(proposal.RecipientName, 200) ?? fullName;
                createdClient = new ClientAccount
                {
                    Name = name,
                    Slug = await UniqueSlugAsync(name, ct),
                    Industry = company?.Industry,
                    Website = company?.Domain is { } domain ? $"https://{domain}" : null,
                    CountryCode = company?.CountryCode ?? "US",
                    Currency = proposal.Currency,
                    Status = ClientAccountStatus.Onboarding,
                    AccountManagerUserId = deal?.OwnerUserId,
                    BillingEmail = proposal.SignerEmail,
                    CrmCompanyId = company?.Id,
                };
                db.Set<ClientAccount>().Add(createdClient);
                clientId = createdClient.Id;
                if (company is not null) company.ClientAccountId = clientId;
                audit.RecordSystem("clients.client_created_from_proposal", nameof(ClientAccount), createdClient.Id,
                    new { createdClient.Name, createdClient.Slug, ProposalId = proposal.Id });
                if (proposal.SignerEmail is { } signerEmail && CrmNormalization.IsValidEmail(signerEmail))
                    inviteEmail = await AddSignerAsync(createdClient.Id, signerEmail, fullName, ct);
            }
            proposal.ClientAccountId = clientId;
            if (deal is not null) deal.ClientAccountId ??= clientId;
            if (proposal.ContactId is { } contactId)
                await db.Set<CrmContact>().Where(c => c.Id == contactId && CrmService.PreCustomerStages.Contains(c.LifecycleStage))
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.LifecycleStage, LifecycleStage.Customer), ct);

            // ---- Contracts: one per billing frequency of the recurring lines
            var invoiceOnAcceptance = proposal.InvoiceOnAcceptance ?? settings.InvoiceOnAcceptance;
            foreach (var group in lines.Where(l => l.Recurrence != Recurrence.OneTime).GroupBy(l => l.Recurrence))
            {
                var frequency = group.Key switch
                {
                    Recurrence.Quarterly => BillingFrequency.Quarterly,
                    Recurrence.Annually => BillingFrequency.Annually,
                    _ => BillingFrequency.Monthly,
                };
                var contract = new Contract
                {
                    Number = await numbers.NextAsync(db, DocumentNumberService.ContractSeries, settings.ContractPrefix, settings.NumberPadding, ct),
                    Title = $"{proposal.Title} — {frequency} retainer",
                    ClientAccountId = clientId.Value,
                    Status = ContractStatus.Active,
                    Currency = proposal.Currency,
                    StartDate = today,
                    BillingFrequency = frequency,
                    AutoRenew = true,
                    PaymentTermsDays = settings.PaymentTermsDays,
                    // The first period is billed on the acceptance invoice.
                    NextPeriodIndex = invoiceOnAcceptance ? 1 : 0,
                    ProposalId = proposal.Id,
                    ProposalVersion = version.VersionNumber,
                    ActivatedAt = Now,
                    Notes = $"Created from proposal {proposal.Number} v{version.VersionNumber}, accepted by {fullName} ({title}).",
                };
                var position = 1;
                foreach (var line in group)
                {
                    var copy = LineBuilder.Copy(line, () => new ContractLine(), proposal.Currency);
                    copy.Position = position++;
                    contract.Lines.Add(copy);
                }
                db.Set<Contract>().Add(contract);
                audit.RecordSystem("billing.contract_created", nameof(Contract), contract.Id,
                    new { contract.Number, contract.BillingFrequency, PerPeriod = contract.Lines.Sum(l => l.Total), ProposalId = proposal.Id });
                contractsCreated++;
            }

            // ---- First invoice: one-time lines + first period of each recurring line
            if (invoiceOnAcceptance && lines.Count > 0)
            {
                invoice = new Invoice
                {
                    ClientAccountId = clientId.Value,
                    Currency = proposal.Currency,
                    PaymentTermsDays = settings.PaymentTermsDays,
                    ProposalId = proposal.Id,
                    IdempotencyKey = $"proposal:{proposal.Id}:initial",
                    Reference = proposal.Number,
                    Notes = $"Proposal {proposal.Number}: {proposal.Title} (one-time items and first billing period).",
                };
                var position = 1;
                foreach (var line in lines)
                {
                    var copy = LineBuilder.Copy(line, () => new InvoiceLine(), proposal.Currency);
                    copy.Position = position++;
                    if (line.Recurrence != Recurrence.OneTime) copy.Description = $"{line.Description} (first {line.Recurrence.ToString().ToLowerInvariant()} period)";
                    invoice.Lines.Add(copy);
                }
                LineBuilder.ApplyTotals(invoice, LineBuilder.TotalsOf(invoice.Lines, invoice.Currency));
                db.Set<Invoice>().Add(invoice);
                audit.RecordSystem("billing.invoice_created", nameof(Invoice), invoice.Id, new { ProposalId = proposal.Id, invoice.Total, invoice.Currency });
                if (settings.AutoIssueInvoices && invoice.Total > 0)
                {
                    await invoices.IssueTrackedAsync(invoice, settings, actor: null, ct);
                    invoice.SentAt = Now;
                    afterCommit = await invoices.DeliverAsync(invoice, reminderKind: null, ct);
                }
            }

            audit.Record("crm.proposal_accepted", nameof(Proposal), proposal.Id, new { Status = ProposalStatus.Sent },
                new { proposal.Status, Version = version.VersionNumber, SignerName = fullName, SignerTitle = title, proposal.SignerIpHash,
                    ClientAccountId = clientId, ContractsCreated = contractsCreated, InvoiceId = invoice?.Id });
            await NotifyTeamAsync(proposal, deal, $"Proposal {proposal.Number} accepted",
                $"{fullName} ({title}) accepted \"{proposal.Title}\" (version {version.VersionNumber}).", BillingNotificationTypes.ProposalAccepted, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw DomainException.Conflict("proposal.already_accepted", "This proposal has already been accepted.");
            }
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();

        // ---- After commit: events, invitation and emails (never inside the transaction)
        if (afterCommit is not null) await afterCommit(ct);
        if (createdClient is not null)
            await events.PublishAsync(new ClientAccountCreated(createdClient.Id, createdClient.CrmCompanyId, proposal.Id, Now), ct);
        await events.PublishAsync(new ProposalAccepted(proposal.Id, proposal.ClientAccountId, proposal.DealId, Now), ct);
        var invited = false;
        if (inviteEmail is not null)
        {
            try
            {
                // Reuses the password-reset flow: the new client user has no usable password until they choose one.
                await auth.ForgotPasswordAsync(inviteEmail, ct);
                invited = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Client invitation for proposal {ProposalId} could not be sent", proposal.Id);
            }
        }
        var view = await proposals.PublicViewAsync(await db.Set<Proposal>().AsNoTracking().FirstAsync(p => p.Id == proposalId, ct), ct);
        return new AcceptProposalResponse(view, createdClient is not null, invited, contractsCreated, invoice is not null);
    }

    public async Task<PublicProposalDto> DeclineAsync(Guid proposalId, DeclineProposalRequest r, SignerContext signer, CancellationToken ct)
    {
        var reason = r.Reason.Trim();
        var today = BillingDates.Today(clock);
        await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
        {
            if (!await dialect.LockRowAsync(db, "proposals", proposalId, ct)) throw DomainException.NotFound("Proposal");
            var proposal = await db.Set<Proposal>().FirstAsync(p => p.Id == proposalId, ct);
            var version = await EnsureRespondableAsync(proposal, r.Version, today, ct);
            proposal.Status = ProposalStatus.Declined;
            proposal.DeclinedAt = Now;
            proposal.DeclineReason = reason;
            CrmDeal? deal = proposal.DealId is { } dealId ? await db.Set<CrmDeal>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealId, ct) : null;
            if (deal is not null)
                db.Set<CrmActivity>().Add(new CrmActivity
                {
                    Type = ActivityType.Note, IsSystem = true, DealId = deal.Id, CompanyId = deal.CompanyId,
                    Subject = $"Proposal {proposal.Number} v{version.VersionNumber} declined", Body = reason,
                });
            audit.Record("crm.proposal_declined", nameof(Proposal), proposalId, new { Status = ProposalStatus.Sent },
                new { proposal.Status, Version = version.VersionNumber, IpHash = privacyHasher.Hash(signer.IpAddress), UserId = signer.UserId }, reason);
            await NotifyTeamAsync(proposal, deal, $"Proposal {proposal.Number} declined", $"\"{proposal.Title}\" was declined: {reason}",
                BillingNotificationTypes.ProposalDeclined, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        return await proposals.PublicViewAsync(await db.Set<Proposal>().AsNoTracking().FirstAsync(p => p.Id == proposalId, ct), ct);
    }

    /// <summary>Validates (under the row lock) that the client may still respond to exactly this version.</summary>
    private async Task<ProposalVersion> EnsureRespondableAsync(Proposal proposal, int requestedVersion, DateOnly today, CancellationToken ct)
    {
        switch (proposal.Status)
        {
            case ProposalStatus.Accepted:
                throw DomainException.Conflict("proposal.already_accepted", "This proposal has already been accepted.");
            case ProposalStatus.Declined:
                throw DomainException.Conflict("proposal.already_declined", "This proposal was declined.");
            case ProposalStatus.Draft when proposal.SentVersion is not null:
                throw DomainException.Conflict("proposal.being_revised", "This proposal is being revised. You'll receive the new version shortly.");
            case ProposalStatus.Expired:
                throw DomainException.Conflict("proposal.expired", "This proposal has expired. Ask us for an updated proposal.");
            case ProposalStatus.Sent or ProposalStatus.Viewed:
                break;
            default:
                throw DomainException.Conflict("proposal.not_open", "This proposal is no longer open for a response.");
        }
        if (proposal.SentVersion != requestedVersion)
            throw DomainException.Conflict("proposal.version_mismatch",
                "A newer version of this proposal was sent. Reload the page to review the latest version.");
        var version = await db.Set<ProposalVersion>().FirstAsync(v => v.ProposalId == proposal.Id && v.VersionNumber == requestedVersion, ct);
        if (version.ValidUntil < today)
            throw DomainException.Conflict("proposal.expired", "This proposal has expired. Ask us for an updated proposal.");
        return version;
    }

    /// <summary>Adds the signer as the client's Owner. New users get the Client role and an unusable password (invitation follows).</summary>
    private async Task<string?> AddSignerAsync(Guid clientId, string email, string displayName, CancellationToken ct)
    {
        var normalized = Normalization.Email(email);
        var user = await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is not null)
        {
            // Never turn a staff/participant account into a client user implicitly.
            if (!user.Roles.All(r => r.Role == Role.Client))
            {
                logger.LogWarning("Proposal signer {Email} already has a non-client account; not added to the client organization", email.Split('@').Last());
                return null;
            }
            if (user.Roles.Count == 0) user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Client, GrantedAt = Now });
            db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = ClientMemberRole.Owner, AddedAt = Now });
            return null; // existing users already have a password
        }
        user = new User
        {
            Email = email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = displayName.Length > 100 ? displayName[..100] : displayName,
            CountryCode = "US",
            ReferralCode = "CL" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
        };
        // Unusable password: a random secret nobody knows; the client sets their own through the reset link.
        user.PasswordHash = passwordHasher.HashPassword(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        user.Roles.Add(new UserRole { UserId = user.Id, Role = Role.Client, GrantedAt = Now });
        db.Set<User>().Add(user);
        db.Set<ClientMember>().Add(new ClientMember { ClientAccountId = clientId, UserId = user.Id, Role = ClientMemberRole.Owner, AddedAt = Now });
        audit.RecordSystem("clients.client_user_invited", nameof(User), user.Id, new { ClientAccountId = clientId, Role = ClientMemberRole.Owner });
        await notifications.StageAsync(new NotificationRequest(user.Id, BillingNotificationTypes.ProposalAccepted, "Welcome to Optimize All",
            "Your client portal is ready. Use the link in the password email to choose your password and sign in.", "/client"), ct);
        return user.Email;
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        while (baseSlug.Contains("--", StringComparison.Ordinal)) baseSlug = baseSlug.Replace("--", "-", StringComparison.Ordinal);
        baseSlug = baseSlug.Trim('-');
        if (baseSlug.Length == 0) baseSlug = "client";
        if (baseSlug.Length > 100) baseSlug = baseSlug[..100].Trim('-');
        var slug = baseSlug;
        for (var i = 2; await db.Set<ClientAccount>().AnyAsync(c => c.Slug == slug, ct); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }

    private async Task NotifyTeamAsync(Proposal proposal, CrmDeal? deal, string title, string body, string type, CancellationToken ct)
    {
        var recipients = new[] { deal?.OwnerUserId, proposal.SentByUserId, proposal.CreatedByUserId }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        foreach (var userId in recipients)
            await notifications.StageAsync(new NotificationRequest(userId, type, title, body, BillingLinks.AgencyProposal(proposal.Id),
                new[] { NotificationChannel.Email }), ct);
    }
}
