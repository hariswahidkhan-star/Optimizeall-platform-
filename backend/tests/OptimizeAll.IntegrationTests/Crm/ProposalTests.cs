using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;
using static OptimizeAll.IntegrationTests.Crm.CrmBillingKit;

namespace OptimizeAll.IntegrationTests.Crm;

public sealed class ProposalTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Sales, JsonElement Deal)> DealAsync(string currency = "USD")
    {
        var (_, sales) = await api.CreateClientAsync(Role.SalesRep);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var company = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/companies", new
        {
            name = $"Prospect {suffix}", domain = $"prospect-{suffix}.example", countryCode = "GB", industry = "saas",
        })).ReadJsonAsync();
        var contact = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/contacts", new
        {
            firstName = "Petra", lastName = "Prospect", email = $"petra@prospect-{suffix}.example", companyId = company.GetGuid("id"),
        })).ReadJsonAsync();
        var deal = await (await sales.PostAsJsonAsync("/api/v1/agency/crm/deals", new
        {
            title = $"Prospect {suffix} retainer", currency, value = 30000m, companyId = company.GetGuid("id"), primaryContactId = contact.GetGuid("id"),
        })).ReadJsonAsync();
        return (sales, deal);
    }

    private async Task<Guid> TaxRateAsync(string name) =>
        await api.WithDbAsync(db => db.Set<TaxRate>().Where(t => t.Name == name).Select(t => t.Id).FirstAsync());

    private object ProposalBody(Guid dealId, DateOnly? validUntil = null, decimal retainer = 2500m, Guid? stamp = null) => new
    {
        title = "Growth retainer", dealId, validUntil = (validUntil ?? api.Today().AddDays(14)).Iso(),
        executiveSummary = "Grow qualified demand.", goals = "More leads", scope = "SEO + ads", deliverables = "Monthly plan",
        timeline = "Start in 2 weeks", terms = "30 days notice",
        lines = new[]
        {
            Line("SEO retainer", 1, retainer, "Monthly", serviceSlug: "seo"),
            Line("Ads management", 1, 1500m, "Quarterly", serviceSlug: "paid-ads"),
            Line("Onboarding", 1, 2000m, "OneTime", "Percent", 10m),
        },
        concurrencyStamp = stamp,
    };

    private static async Task<(JsonElement Proposal, string Token)> SendAsync(HttpClient sales, JsonElement proposal, bool email = true)
    {
        var sent = await (await sales.PostAsJsonAsync($"/api/v1/agency/proposals/{proposal.GetGuid("id")}/send",
            new { concurrencyStamp = proposal.GetGuid("concurrencyStamp"), email })).ReadJsonAsync();
        var url = sent.Str("shareUrl");
        return (sent.GetProperty("proposal"), url[(url.LastIndexOf('/') + 1)..]);
    }

    private static object Acceptance(int version = 1, string? email = null) =>
        new { version, fullName = "Petra Prospect", title = "CEO", email, agreeToTerms = true };

    [Fact]
    public async Task Preview_computes_discounts_taxes_recurring_and_currency_rounding()
    {
        var (sales, _) = await DealAsync();
        var vat = await TaxRateAsync("UK VAT 20%");
        var preview = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals/preview", new
        {
            currency = "GBP",
            lines = new[]
            {
                Line("Retainer", 1, 999.99m, "Monthly", taxRateId: vat),
                Line("Setup", 3, 333.33m, "OneTime", "Amount", 99.99m, vat),
            },
        })).ReadJsonAsync();
        var totals = preview.GetProperty("totals");
        Assert.Equal(1999.98m, totals.Dec("grossTotal"));
        Assert.Equal(99.99m, totals.Dec("discountTotal"));
        Assert.Equal(1899.99m, totals.Dec("subtotal"));
        Assert.Equal(380.00m, totals.Dec("taxTotal")); // 200.00 + 180.00
        Assert.Equal(2279.99m, totals.Dec("total"));
        Assert.Equal(1199.99m, preview.GetProperty("recurring").Dec("monthlyTotal"));

        var jpy = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals/preview", new
        {
            currency = "JPY", lines = new[] { Line("Ads", 3, 1234.5m, "Monthly", "Percent", 10m) },
        })).ReadJsonAsync();
        Assert.Equal(3334m, jpy.GetProperty("totals").Dec("total")); // 3 × 1234.5 = 3703.5 → 3704; 10% = 370.4 → 370
        var kwd = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals/preview", new
        {
            currency = "KWD", lines = new[] { Line("Ads", 1, 100.0005m, "OneTime") },
        })).ReadJsonAsync();
        Assert.Equal(100.001m, kwd.GetProperty("totals").Dec("total"));

        await (await sales.PostAsJsonAsync("/api/v1/agency/proposals/preview", new { currency = "USD", lines = new[] { Line("Bad", 0, 10m) } }))
            .ShouldFailAsync(400, "billing.invalid_quantity");
        await (await sales.PostAsJsonAsync("/api/v1/agency/proposals/preview", new
        {
            currency = "USD", lines = new[] { Line("Bad tax", 1, 10m, taxRateId: Guid.NewGuid()) },
        })).ShouldFailAsync(400, "billing.invalid_tax_rate");
    }

    [Fact]
    public async Task Editing_a_sent_proposal_creates_a_new_version_and_the_client_must_accept_the_latest()
    {
        var (sales, deal) = await DealAsync();
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id")))).ReadJsonAsync();
        Assert.Equal("Draft", created.Str("status"));
        Assert.Equal(1, created.GetProperty("currentVersion").GetInt32());
        Assert.StartsWith("PR-", created.Str("number"));

        // Editing an unsent draft stays on version 1.
        var edited = await (await sales.PutAsJsonAsync($"/api/v1/agency/proposals/{created.GetGuid("id")}",
            ProposalBody(deal.GetGuid("id"), retainer: 2600m, stamp: created.GetGuid("concurrencyStamp")))).ReadJsonAsync();
        Assert.Equal(1, edited.GetProperty("currentVersion").GetInt32());
        Assert.Equal(1, edited.GetProperty("versions").GetArrayLength());

        var (sent, token) = await SendAsync(sales, edited);
        Assert.Equal("Sent", sent.Str("status"));
        Assert.True(Directory.Exists(api.MailDirectory) && Directory.GetFiles(api.MailDirectory).Length > 0, "the proposal email was written");

        var revised = await (await sales.PutAsJsonAsync($"/api/v1/agency/proposals/{created.GetGuid("id")}",
            ProposalBody(deal.GetGuid("id"), retainer: 2400m, stamp: sent.GetGuid("concurrencyStamp")))).ReadJsonAsync();
        Assert.Equal(2, revised.GetProperty("currentVersion").GetInt32());
        Assert.Equal("Draft", revised.Str("status"));
        Assert.Equal(2, revised.GetProperty("versions").GetArrayLength());
        var v1 = await (await sales.GetAsync($"/api/v1/agency/proposals/{created.GetGuid("id")}/versions/1")).ReadJsonAsync();
        Assert.Equal(2600m, v1.GetProperty("version").GetProperty("lines")[0].Dec("unitPrice"));

        // While revising, the public page shows the sent version and doesn't accept responses.
        var anonymous = api.CreateClient();
        var page = await (await anonymous.GetAsync($"/api/v1/public/proposals/{token}")).ReadJsonAsync();
        Assert.True(page.GetProperty("beingRevised").GetBoolean());
        Assert.False(page.GetProperty("canRespond").GetBoolean());
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance(1))).ShouldFailAsync(409, "proposal.being_revised");

        var (resent, sameToken) = await SendAsync(sales, revised, email: false);
        Assert.Equal(token, sameToken);
        Assert.Equal(2, resent.GetProperty("sentVersion").GetInt32());
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance(1))).ShouldFailAsync(409, "proposal.version_mismatch");
        page = await (await anonymous.GetAsync($"/api/v1/public/proposals/{token}")).ReadJsonAsync();
        Assert.Equal(2, page.GetProperty("version").GetProperty("versionNumber").GetInt32());
        Assert.Equal(2400m, page.GetProperty("version").GetProperty("lines")[0].Dec("unitPrice"));
    }

    [Fact]
    public async Task Public_view_counts_views_and_acceptance_creates_client_contracts_invoice_and_invitation_once()
    {
        var (sales, deal) = await DealAsync();
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id")))).ReadJsonAsync();
        var (_, token) = await SendAsync(sales, created);
        var anonymous = api.CreateClient();
        anonymous.DefaultRequestHeaders.UserAgent.ParseAdd("ProposalTest/1.0");

        await (await anonymous.GetAsync("/api/v1/public/proposals/not-a-real-token")).ShouldFailAsync(404);
        var page = await (await anonymous.GetAsync($"/api/v1/public/proposals/{token}")).ReadJsonAsync();
        await anonymous.GetAsync($"/api/v1/public/proposals/{token}");
        Assert.True(page.GetProperty("canRespond").GetBoolean());
        Assert.Equal("Growth retainer", page.Str("title"));
        var staffView = await (await sales.GetAsync($"/api/v1/agency/proposals/{created.GetGuid("id")}")).ReadJsonAsync();
        Assert.Equal(2, staffView.GetProperty("viewCount").GetInt32());
        Assert.Equal("Viewed", staffView.Str("status"));
        Assert.NotEqual(JsonValueKind.Null, staffView.GetProperty("firstViewedAt").ValueKind);

        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", new { version = 1, fullName = "Petra", title = "CEO", agreeToTerms = false }))
            .ShouldFailAsync(400, "proposal.terms_required");
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", new { version = 1, fullName = "", title = "CEO", agreeToTerms = true }))
            .ShouldFailAsync(400);

        var signerEmail = $"petra.{Guid.NewGuid():N}@signer.example";
        var accepted = await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance(1, signerEmail))).ReadJsonAsync();
        Assert.True(accepted.GetProperty("clientAccountCreated").GetBoolean());
        Assert.True(accepted.GetProperty("invitationSent").GetBoolean());
        Assert.Equal(2, accepted.GetProperty("contractsCreated").GetInt32()); // monthly + quarterly
        Assert.True(accepted.GetProperty("invoiceCreated").GetBoolean());
        Assert.Equal("Accepted", accepted.GetProperty("proposal").Str("status"));

        // Accepting twice (or declining after) is rejected.
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance(1, signerEmail))).ShouldFailAsync(409, "proposal.already_accepted");
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/decline", new { version = 1, reason = "Changed mind" }))
            .ShouldFailAsync(409, "proposal.already_accepted");

        var proposalId = created.GetGuid("id");
        var state = await api.WithDbAsync(async db =>
        {
            var p = await db.Set<Proposal>().AsNoTracking().FirstAsync(x => x.Id == proposalId);
            return new
            {
                Proposal = p,
                Version = await db.Set<ProposalVersion>().AsNoTracking().FirstAsync(v => v.ProposalId == proposalId && v.VersionNumber == 1),
                Clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == p.ClientAccountId).ToListAsync(),
                Contracts = await db.Set<Contract>().AsNoTracking().Include(c => c.Lines).Where(c => c.ProposalId == proposalId).ToListAsync(),
                Invoices = await db.Set<Invoice>().AsNoTracking().Include(i => i.Lines).Where(i => i.ProposalId == proposalId).ToListAsync(),
                Deal = await db.Set<CrmDeal>().AsNoTracking().FirstAsync(d => d.Id == p.DealId),
                Signer = await db.Set<User>().AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.NormalizedEmail == signerEmail.ToUpperInvariant()),
                Members = await db.Set<ClientMember>().AsNoTracking().Where(m => m.ClientAccountId == p.ClientAccountId).ToListAsync(),
                Audit = await db.Set<AuditLog>().AsNoTracking().AnyAsync(a => a.Action == "crm.proposal_accepted" && a.EntityId == proposalId.ToString()),
            };
        });
        Assert.Equal("Petra Prospect", state.Proposal.SignerName);
        Assert.Equal("CEO", state.Proposal.SignerTitle);
        // TestServer requests carry no remote IP; the IP hashing itself is covered by Signer_ip_is_stored_only_as_a_keyed_hash.
        Assert.True(state.Proposal.SignerIpHash is null || state.Proposal.SignerIpHash.Length == 64);
        Assert.Equal("ProposalTest/1.0", state.Proposal.SignerUserAgent);
        Assert.True(state.Version.Locked);
        var client = Assert.Single(state.Clients);
        Assert.Equal(ClientAccountStatus.Onboarding, client.Status);
        Assert.Equal("USD", client.Currency);
        Assert.Equal(2, state.Contracts.Count);
        Assert.All(state.Contracts, c => Assert.Equal(ContractStatus.Active, c.Status));
        Assert.All(state.Contracts, c => Assert.Equal(1, c.NextPeriodIndex));
        Assert.Contains(state.Contracts, c => c.BillingFrequency == BillingFrequency.Monthly && c.Lines.Single().UnitPrice == 2500m);
        Assert.Contains(state.Contracts, c => c.BillingFrequency == BillingFrequency.Quarterly && c.Lines.Single().UnitPrice == 1500m);
        var invoice = Assert.Single(state.Invoices);
        Assert.Equal(3, invoice.Lines.Count);
        Assert.Equal(2500m + 1500m + 1800m, invoice.Total);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(DealStatus.Won, state.Deal.Status);
        Assert.Equal(client.Id, state.Deal.ClientAccountId);
        Assert.NotNull(state.Signer);
        Assert.Contains(state.Signer!.Roles, r => r.Role == Role.Client);
        Assert.Equal(ClientMemberRole.Owner, Assert.Single(state.Members).Role);
        Assert.True(state.Audit);
        // The invitation is the password-reset email (the new client user has no usable password yet).
        var mails = Directory.GetFiles(api.MailDirectory).Select(File.ReadAllText).ToList();
        Assert.Contains(mails, m => m.Contains(signerEmail, StringComparison.OrdinalIgnoreCase) && m.Contains("reset-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Signer_ip_is_stored_only_as_a_keyed_hash()
    {
        var (sales, deal) = await DealAsync();
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id")))).ReadJsonAsync();
        await SendAsync(sales, created, email: false);
        using var scope = api.Services.CreateScope();
        var acceptance = scope.ServiceProvider.GetRequiredService<ProposalAcceptanceService>();
        await acceptance.AcceptAsync(created.GetGuid("id"), new AcceptProposalRequest { Version = 1, FullName = "Petra Prospect", Title = "CEO", AgreeToTerms = true },
            new SignerContext("203.0.113.7", "Browser/2.0", null), CancellationToken.None);
        var expected = scope.ServiceProvider.GetRequiredService<IPrivacyHasher>().Hash("203.0.113.7");
        var stored = await api.WithDbAsync(db => db.Set<Proposal>().Where(p => p.Id == created.GetGuid("id"))
            .Select(p => new { p.SignerIpHash, p.SignerUserAgent }).FirstAsync());
        Assert.Equal(expected, stored.SignerIpHash);
        Assert.DoesNotContain("203.0.113.7", stored.SignerIpHash!);
        Assert.Equal("Browser/2.0", stored.SignerUserAgent);
    }

    [Fact]
    public async Task Concurrent_accepts_produce_exactly_one_acceptance()
    {
        var (sales, deal) = await DealAsync();
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id")))).ReadJsonAsync();
        var (_, token) = await SendAsync(sales, created, email: false);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            api.CreateClient().PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance(1, $"race.{Guid.NewGuid():N}@example.test"))));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var id = created.GetGuid("id");
        var counts = await api.WithDbAsync(async db => (
            await db.Set<Contract>().CountAsync(c => c.ProposalId == id),
            await db.Set<Invoice>().CountAsync(i => i.ProposalId == id),
            await db.Set<ClientAccount>().CountAsync(c => db.Set<Proposal>().Any(p => p.Id == id && p.ClientAccountId == c.Id))));
        Assert.Equal((2, 1, 1), counts);
    }

    [Fact]
    public async Task Expired_proposals_cannot_be_accepted_and_declines_need_a_reason()
    {
        var (sales, deal) = await DealAsync();
        var created = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id"), api.Today()))).ReadJsonAsync();
        var (_, token) = await SendAsync(sales, created, email: false);
        var anonymous = api.CreateClient();
        // The validity date passes (moved in the database: the shared test clock can't go back afterwards).
        var proposalId = created.GetGuid("id");
        var yesterday = api.Today().AddDays(-1);
        await api.WithDbAsync(db => db.Set<ProposalVersion>().Where(v => v.ProposalId == proposalId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ValidUntil, yesterday)));
        var page = await (await anonymous.GetAsync($"/api/v1/public/proposals/{token}")).ReadJsonAsync();
        Assert.True(page.GetProperty("expired").GetBoolean());
        Assert.False(page.GetProperty("canRespond").GetBoolean());
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance())).ShouldFailAsync(409, "proposal.expired");
        await api.RunJobAsync<CrmTaskReminderJob>();
        Assert.Equal(ProposalStatus.Expired, await api.WithDbAsync(db => db.Set<Proposal>().Where(p => p.Id == proposalId).Select(p => p.Status).FirstAsync()));
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{token}/accept", Acceptance())).ShouldFailAsync(409, "proposal.expired");

        var other = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", ProposalBody(deal.GetGuid("id")))).ReadJsonAsync();
        var (_, otherToken) = await SendAsync(sales, other, email: false);
        await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{otherToken}/decline", new { version = 1, reason = "" })).ShouldFailAsync(400);
        var declined = await (await anonymous.PostAsJsonAsync($"/api/v1/public/proposals/{otherToken}/decline",
            new { version = 1, reason = "Went with another agency" })).ReadJsonAsync();
        Assert.Equal("Declined", declined.Str("status"));
        var staff = await (await sales.GetAsync($"/api/v1/agency/proposals/{other.GetGuid("id")}")).ReadJsonAsync();
        Assert.Equal("Went with another agency", staff.Str("declineReason"));
    }

    [Fact]
    public async Task Client_portal_shows_only_own_proposals_to_billing_members()
    {
        var (sales, _) = await DealAsync();
        var orgA = await api.CreateClientAccountAsync("Org A");
        var orgB = await api.CreateClientAccountAsync("Org B");
        var proposal = await (await sales.PostAsJsonAsync("/api/v1/agency/proposals", new
        {
            title = "Org A expansion", clientAccountId = orgA.Id, validUntil = api.Today().AddDays(10).Iso(), recipientEmail = "a@example.test",
            lines = new[] { Line("Extra content", 4, 250m, "Monthly") },
        })).ReadJsonAsync();
        await SendAsync(sales, proposal, email: false);

        var (_, ownerA) = await api.CreateClientMemberAsync(orgA.Id, ClientMemberRole.Owner);
        var (_, viewerA) = await api.CreateClientMemberAsync(orgA.Id, ClientMemberRole.Viewer);
        var (_, billingB) = await api.CreateClientMemberAsync(orgB.Id, ClientMemberRole.Billing);

        var list = await (await ownerA.GetAsync("/api/v1/client/billing/proposals")).ReadJsonAsync();
        Assert.Single(list.EnumerateArray());
        await (await billingB.GetAsync($"/api/v1/client/billing/proposals/{proposal.GetGuid("id")}")).ShouldFailAsync(404);
        Assert.Empty((await (await billingB.GetAsync("/api/v1/client/billing/proposals")).ReadJsonAsync()).EnumerateArray());
        await (await viewerA.GetAsync("/api/v1/client/billing/proposals")).ShouldFailAsync(403, "client.insufficient_role");
        await (await billingB.PostAsJsonAsync($"/api/v1/client/billing/proposals/{proposal.GetGuid("id")}/accept", Acceptance())).ShouldFailAsync(404);

        var accepted = await (await ownerA.PostAsJsonAsync($"/api/v1/client/billing/proposals/{proposal.GetGuid("id")}/accept", Acceptance()))
            .ReadJsonAsync();
        Assert.False(accepted.GetProperty("clientAccountCreated").GetBoolean());
        Assert.Equal(1, accepted.GetProperty("contractsCreated").GetInt32());
        // Staff can't accept on the client's behalf through the client portal.
        await (await sales.GetAsync("/api/v1/client/billing/proposals")).ShouldFailAsync(403);
    }
}
