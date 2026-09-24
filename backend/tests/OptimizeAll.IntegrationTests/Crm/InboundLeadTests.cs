using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Crm;

/// <summary>Website inquiries and landing-page forms become CRM leads exactly once, deduped and assigned round-robin.</summary>
public sealed class InboundLeadTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private WebsiteInquiryReceived Inquiry(string email, string? company = "Acme Corp", string? website = "https://www.acme-inbound.example",
        string type = "free-audit") =>
        new(Guid.NewGuid(), type, "Jane Doe", email, "+1 555 0100", company, website, "We need more leads from Google.",
            new[] { "seo", "paid-ads" }, "5k-10k", "google", "cpc", "brand", null, api.UtcNow());

    [Fact]
    public async Task Inquiry_creates_contact_company_and_deal_once_and_dedupes_by_email_and_domain()
    {
        var rep = await api.CreateUserAsync(new[] { Role.SalesRep });
        var email = $"jane.{Guid.NewGuid():N}@acme-inbound.example";
        var inquiry = Inquiry(email);

        await api.PublishAsync(inquiry);
        await api.PublishAsync(inquiry); // duplicate delivery of the same event

        var (contacts, deals, companies, inbound) = await api.WithDbAsync(async db => (
            await db.Set<CrmContact>().AsNoTracking().Where(c => c.NormalizedEmail == email.ToUpperInvariant()).ToListAsync(),
            await db.Set<CrmDeal>().AsNoTracking().Where(d => d.Source == DealSource.WebsiteInquiry).ToListAsync(),
            await db.Set<CrmCompany>().AsNoTracking().Where(c => c.Domain == "acme-inbound.example").ToListAsync(),
            await db.Set<CrmInboundEvent>().AsNoTracking().CountAsync(e => e.Key == $"inquiry:{inquiry.InquiryId}")));
        var contact = Assert.Single(contacts);
        var company = Assert.Single(companies);
        var deal = Assert.Single(deals, d => d.PrimaryContactId == contact.Id);
        Assert.Equal(1, inbound);
        Assert.Equal(company.Id, contact.CompanyId);
        Assert.Equal("Jane", contact.FirstName);
        Assert.Equal(LifecycleStage.Lead, contact.LifecycleStage);
        Assert.Equal("google", contact.FirstTouch.Source);
        Assert.Equal("google", deal.FirstTouch.Source);
        Assert.Equal("brand", deal.LastTouch.Campaign);
        Assert.Equal(new[] { "seo", "paid-ads" }, deal.ServiceSlugs);
        Assert.Equal("free-audit", deal.SourceDetail);
        Assert.True(contact.Score > 0, "the inquiry engagement and fit rules score the lead");
        var firstStage = await api.WithDbAsync(db => db.Set<PipelineStage>().Where(s => s.Kind == StageKind.Open).OrderBy(s => s.Position).FirstAsync());
        Assert.Equal(firstStage.Id, deal.StageId);
        Assert.NotNull(deal.OwnerUserId);
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>()
            .AnyAsync(n => n.UserId == deal.OwnerUserId && n.Type == BillingNotificationTypes.LeadAssigned)));

        // The same person writing again (new inquiry, different email case, other page) merges into the same records.
        await api.PublishAsync(Inquiry(email.ToUpperInvariant(), company: "ACME", website: null, type: "quote") with
        {
            UtmSource = "newsletter", UtmMedium = "email", UtmCampaign = "october",
        });
        var after = await api.WithDbAsync(async db => (
            await db.Set<CrmContact>().AsNoTracking().CountAsync(c => c.NormalizedEmail == email.ToUpperInvariant()),
            await db.Set<CrmDeal>().AsNoTracking().Where(d => d.PrimaryContactId == contact.Id).ToListAsync(),
            await db.Set<CrmContact>().AsNoTracking().FirstAsync(c => c.Id == contact.Id)));
        Assert.Equal(1, after.Item1);
        var merged = Assert.Single(after.Item2);
        Assert.Equal("google", merged.FirstTouch.Source);
        Assert.Equal("newsletter", merged.LastTouch.Source);
        Assert.Equal("google", after.Item3.FirstTouch.Source);
        Assert.Equal("october", after.Item3.LastTouch.Campaign);
        _ = rep;
    }

    [Fact]
    public async Task A_booked_consultation_and_a_confirmed_newsletter_signup_count_as_engagement()
    {
        await api.CreateUserAsync(new[] { Role.SalesRep });
        var email = $"booker.{Guid.NewGuid():N}@acme-inbound.example";
        var booking = Inquiry(email, type: "Consultation");

        await api.PublishAsync(booking);
        await api.PublishAsync(booking); // duplicate delivery
        await api.PublishAsync(new NewsletterSubscribed(Guid.NewGuid(), email.ToUpperInvariant(), api.UtcNow()));

        var types = await api.WithDbAsync(async db =>
        {
            var contactId = await db.Set<CrmContact>().Where(c => c.NormalizedEmail == email.ToUpperInvariant()).Select(c => c.Id).SingleAsync();
            return await db.Set<CrmEngagement>().AsNoTracking().Where(e => e.ContactId == contactId).Select(e => e.Type).ToListAsync();
        });
        Assert.Equal(new[] { "meeting_booked", "newsletter_subscribed", "website_inquiry" }, types.Order().ToArray());
    }

    [Fact]
    public async Task Leads_are_assigned_round_robin_to_sales_reps_and_account_managers()
    {
        var rep = await api.CreateUserAsync(new[] { Role.SalesRep });
        var manager = await api.CreateUserAsync(new[] { Role.AccountManager });
        await api.CreateUserAsync(new[] { Role.Strategist }); // crm.view only: never assigned
        var eligible = await api.WithDbAsync(db => db.Set<User>().Where(u => u.Status == UserStatus.Active &&
            u.Roles.Any(r => r.Role == Role.SalesRep || r.Role == Role.AccountManager)).Select(u => u.Id).ToListAsync());

        var owners = new List<Guid>();
        for (var i = 0; i < eligible.Count * 2; i++)
        {
            var e = Inquiry($"rr{i}.{Guid.NewGuid():N}@roundrobin{i}.example", company: $"RR {i}", website: null);
            await api.PublishAsync(e);
            owners.Add(await api.WithDbAsync(db => db.Set<CrmInboundEvent>().Where(x => x.Key == $"inquiry:{e.InquiryId}")
                .Select(x => x.AssignedUserId!.Value).FirstAsync()));
        }
        // Every eligible user got exactly two leads, in a repeating order.
        Assert.All(eligible, id => Assert.Equal(2, owners.Count(o => o == id)));
        Assert.Equal(owners.Take(eligible.Count), owners.Skip(eligible.Count));
        Assert.Contains(rep.Id, owners);
        Assert.Contains(manager.Id, owners);
    }

    [Fact]
    public async Task Concurrent_duplicate_events_create_one_deal()
    {
        await api.CreateUserAsync(new[] { Role.SalesRep });
        var email = $"burst.{Guid.NewGuid():N}@burst.example";
        var e = Inquiry(email, company: "Burst Ltd", website: "burst.example");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.PublishAsync(e)));
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => api.PublishAsync(Inquiry(email, company: "Burst Ltd", website: "burst.example"))));
        var counts = await api.WithDbAsync(async db => (
            await db.Set<CrmContact>().CountAsync(c => c.NormalizedEmail == email.ToUpperInvariant()),
            await db.Set<CrmCompany>().CountAsync(c => c.Domain == "burst.example"),
            await db.Set<CrmDeal>().CountAsync(d => d.Title.StartsWith("Burst Ltd"))));
        Assert.Equal((1, 1, 1), counts);
    }

    [Fact]
    public async Task Agency_forms_create_leads_but_client_forms_are_ignored()
    {
        var email = $"form.{Guid.NewGuid():N}@formco.example";
        await api.PublishAsync(new FormSubmitted(Guid.NewGuid(), Guid.NewGuid(), null, email, "Sam Form", null,
            new Dictionary<string, string> { ["company"] = "FormCo", ["budget"] = "2k-5k", ["services"] = "seo, social-media" }, "linkedin", "paid", "q4",
            api.UtcNow()));
        var client = await api.CreateClientAccountAsync();
        var clientLead = $"client.lead.{Guid.NewGuid():N}@example.test";
        await api.PublishAsync(new FormSubmitted(Guid.NewGuid(), Guid.NewGuid(), client.Id, clientLead, "Client Lead", null,
            new Dictionary<string, string>(), null, null, null, api.UtcNow()));

        var deal = await api.WithDbAsync(db => db.Set<CrmDeal>().AsNoTracking().FirstAsync(d => d.Title.StartsWith("FormCo")));
        Assert.Equal(DealSource.Form, deal.Source);
        Assert.Equal("linkedin", deal.FirstTouch.Source);
        Assert.Equal(new[] { "seo", "social-media" }, deal.ServiceSlugs);
        Assert.False(await api.WithDbAsync(db => db.Set<CrmContact>().AnyAsync(c => c.NormalizedEmail == clientLead.ToUpperInvariant())));
    }
}
