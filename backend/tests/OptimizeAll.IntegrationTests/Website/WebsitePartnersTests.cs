using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Website;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// Partners and sponsored placements: the seeded partners (PCI AI, Certuvo) and their public profile/partners pages with
/// valid schema.org JSON-LD, admin CRUD (validation, 409 on stale stamps and duplicate slugs, audit, 403), links hidden
/// while a partner has no website, targeting of ad units, the impression beacon and click redirect (bots never counted),
/// the report and CSV export, the offer rules, the sitemap and slug-rename redirects.
/// </summary>
public sealed class WebsitePartnersTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Admin = "/api/v1/agency/website/partners";
    private const string Public = "/api/v1/public/partners";
    private const string Browser = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}";

    private static Dictionary<string, object?> Partner(string slug, string? website = null, params string[] slots) => new()
    {
        ["slug"] = slug,
        ["name"] = "Partner " + slug,
        ["logoUrl"] = "/partners/pci-ai.png",
        ["websiteUrl"] = website,
        ["tagline"] = "A test partner",
        ["descriptionMarkdown"] = "Partner **" + slug + "** is a test organization.",
        ["keywords"] = new[] { "quantum widgets " + slug },
        ["categories"] = new[] { slug },
        ["slots"] = slots,
        ["isActive"] = true,
        ["sortOrder"] = 500,
    };

    private HttpClient Visitor(string userAgent = Browser)
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "tests");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    // ---------------------------------------------------------------- seeded partners, public pages, JSON-LD

    [Fact]
    public async Task The_seeded_partners_are_live_with_the_partnership_statement_and_cross_linked_profiles()
    {
        var directory = await api.Anonymous().GetJsonAsync(Public);
        var partners = directory.GetProperty("partners").EnumerateArray().ToList();
        var pci = partners.Single(p => p.GetProperty("slug").GetString() == "pci-ai");
        var certuvo = partners.Single(p => p.GetProperty("slug").GetString() == "certuvo");
        Assert.Equal("Optimize All is the official marketing partner of PCI AI", pci.GetProperty("relationshipLabel").GetString());
        Assert.Equal("Optimize All is the official marketing partner of Certuvo", certuvo.GetProperty("relationshipLabel").GetString());
        Assert.Equal("pciai.org", pci.GetProperty("websiteHost").GetString());
        Assert.Equal("/api/v1/public/partners/pci-ai/visit", pci.GetProperty("visitUrl").GetString());
        Assert.Equal("/partners/pci-ai.png", pci.GetProperty("logoUrl").GetString());
        Assert.Contains(PartnerSlots.HomeStrip, pci.GetProperty("slots").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains(PartnerSlots.Footer, certuvo.GetProperty("slots").EnumerateArray().Select(s => s.GetString()));
        // The seeded launch offer is unconfirmed: never shown until an editor checks it is still running.
        Assert.Equal(JsonValueKind.Null, certuvo.GetProperty("offer").ValueKind);
        var rules = directory.GetProperty("linkRules").EnumerateArray().ToDictionary(r => r.GetProperty("slug").GetString()!, r => r.GetProperty("host").GetString());
        Assert.Equal("pciai.org", rules["pci-ai"]);
        Assert.Equal("certuvo.com", rules["certuvo"]);
        // Other tests of this class add partners, so only the start of the list is fixed.
        Assert.Matches("^Optimize All is the official marketing partner of PCI AI(,| and) Certuvo",
            directory.GetProperty("seo").GetProperty("description").GetString());

        var profile = await api.Anonymous().GetJsonAsync($"{Public}/pci-ai");
        Assert.Equal("PCI AI — PCL-AI, PFL-AI and PML-AI certifications", profile.GetProperty("seo").GetProperty("title").GetString());
        Assert.Equal("/partners/pci-ai.png", profile.GetProperty("seo").GetProperty("ogImageUrl").GetString());
        Assert.Equal("http://app.test/partners/pci-ai", profile.GetProperty("seo").GetProperty("canonicalUrl").GetString());
        Assert.Equal(new[] { "pcl-ai", "pfl-ai", "pml-ai" },
            profile.GetProperty("offerings").EnumerateArray().Select(o => o.GetProperty("anchor").GetString()));
        Assert.All(profile.GetProperty("offerings").EnumerateArray(), o =>
            Assert.Contains("USD 350 exam fee", o.GetProperty("facts").EnumerateArray().Select(f => f.GetString())));
        Assert.Contains("[Certuvo](/partners/certuvo)", profile.GetProperty("descriptionMarkdown").GetString());
        Assert.Equal("certuvo", profile.GetProperty("related")[0].GetProperty("slug").GetString());

        var cert = await api.Anonymous().GetJsonAsync($"{Public}/certuvo");
        Assert.Equal("pci-ai", cert.GetProperty("related")[0].GetProperty("slug").GetString());
        var exams = cert.GetProperty("offerings").EnumerateArray().Select(o => o.GetProperty("title").GetString()).ToList();
        Assert.Equal(new[] { "CIA", "CISA", "CMA", "CPA", "CFA", "PMP", "NCLEX-RN", "NCLEX-PN", "PCL-AI", "PFL-AI", "PML-AI" }, exams.Skip(1));
        Assert.Equal("/partners/pci-ai#pcl-ai", cert.GetProperty("offerings").EnumerateArray()
            .Single(o => o.GetProperty("title").GetString() == "PCL-AI").GetProperty("link").GetString());
        Assert.DoesNotContain("ACCA", cert.GetRawText());

        await api.Anonymous().GetJsonAsync($"{Public}/no-such-partner", 404);
    }

    /// <summary>Properties each schema.org type may carry on the partner pages (all valid per schema.org).</summary>
    private static readonly Dictionary<string, HashSet<string>> Allowed = new()
    {
        ["Organization"] = new() { "@context", "@type", "@id", "name", "url", "logo", "image", "description", "slogan", "sameAs", "knowsAbout" },
        ["WebPage"] = new() { "@context", "@type", "@id", "url", "name", "description", "about", "primaryImageOfPage", "publisher", "dateModified" },
        ["CollectionPage"] = new() { "@context", "@type", "@id", "url", "name", "description", "publisher", "mentions", "mainEntity" },
        ["BreadcrumbList"] = new() { "@context", "@type", "itemListElement" },
        ["ItemList"] = new() { "@type", "numberOfItems", "itemListElement" },
        ["ListItem"] = new() { "@type", "position", "name", "url", "item" },
        ["ImageObject"] = new() { "@type", "url" },
    };

    private static void AssertValidJsonLd(JsonElement node, List<string> types, bool root = true)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) AssertValidJsonLd(item, types, false);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;
        if (root) Assert.Equal("https://schema.org", node.GetProperty("@context").GetString());
        var names = node.EnumerateObject().Select(p => p.Name).ToList();
        if (node.TryGetProperty("@type", out var type))
        {
            var t = type.GetString()!;
            types.Add(t);
            Assert.True(Allowed.ContainsKey(t), $"Unexpected @type {t}");
            Assert.Empty(names.Except(Allowed[t]));
        }
        else
        {
            Assert.Equal(new[] { "@id" }, names); // a reference
        }
        Assert.DoesNotContain(names, n => n is "sponsor" or "funder" or "member" or "memberOf");
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Name is "url" or "logo" or "image" or "@id" or "item" && prop.Value.ValueKind == JsonValueKind.String)
                Assert.True(Uri.IsWellFormedUriString(prop.Value.GetString(), UriKind.Absolute), $"{prop.Name} is not absolute: {prop.Value}");
            AssertValidJsonLd(prop.Value, types, false);
        }
    }

    [Fact]
    public async Task Partner_pages_carry_schema_valid_json_ld_with_the_partner_as_organization_and_no_sponsorship_claims()
    {
        var profile = await api.Anonymous().GetJsonAsync($"{Public}/certuvo");
        var types = new List<string>();
        foreach (var item in profile.GetProperty("jsonLd").EnumerateArray()) AssertValidJsonLd(item, types);
        Assert.Equal(new[] { "BreadcrumbList", "ImageObject", "ListItem", "Organization", "WebPage" }, types.Distinct().Order());

        var org = profile.GetProperty("jsonLd")[0];
        Assert.Equal("Organization", org.GetProperty("@type").GetString());
        Assert.Equal("Certuvo", org.GetProperty("name").GetString());
        Assert.Equal("https://certuvo.com", org.GetProperty("url").GetString());
        Assert.Equal("http://app.test/partners/certuvo.jpg", org.GetProperty("logo").GetString());
        Assert.Contains("https://certuvo.com", org.GetProperty("sameAs").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains("CPA exam prep", org.GetProperty("knowsAbout").EnumerateArray().Select(s => s.GetString()));
        var page = profile.GetProperty("jsonLd")[1];
        Assert.Equal(org.GetProperty("@id").GetString(), page.GetProperty("about").GetProperty("@id").GetString());
        Assert.Equal("http://app.test/#organization", page.GetProperty("publisher").GetProperty("@id").GetString());

        var directory = await api.Anonymous().GetJsonAsync(Public);
        types.Clear();
        foreach (var item in directory.GetProperty("jsonLd").EnumerateArray()) AssertValidJsonLd(item, types);
        var collection = directory.GetProperty("jsonLd")[0];
        Assert.Equal("CollectionPage", collection.GetProperty("@type").GetString());
        var mentioned = collection.GetProperty("mentions").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToList();
        Assert.Contains("PCI AI", mentioned);
        Assert.Contains("Certuvo", mentioned);
        Assert.Contains(collection.GetProperty("mainEntity").GetProperty("itemListElement").EnumerateArray(),
            i => i.GetProperty("url").GetString() == "http://app.test/partners/pci-ai");

        // Our own Organization object makes no (invalid) partnership claims either.
        var home = await api.Anonymous().GetJsonAsync("/api/v1/public/home");
        Assert.DoesNotContain("sponsor", home.GetProperty("jsonLd").GetRawText());
        Assert.DoesNotContain("\"member", home.GetProperty("jsonLd").GetRawText());
    }

    [Fact]
    public async Task The_sitemap_lists_the_partners_page_and_active_indexable_profiles_only()
    {
        var admin = await api.AdminAsync();
        var hidden = Unique("hidden");
        var noindex = Unique("noindex");
        var inactive = Partner(hidden);
        inactive["isActive"] = false;
        await admin.PostJsonAsync(Admin, inactive, 201);
        var quiet = Partner(noindex);
        quiet["seo"] = new { noIndex = true };
        await admin.PostJsonAsync(Admin, quiet, 201);

        // The sitemap index lists the partners sitemap, which lists the partner pages (the flat legacy sitemap too).
        var index = await api.Anonymous().GetStringAsync("/sitemap.xml");
        Assert.Contains("<loc>http://app.test/sitemaps/partners.xml</loc>", index);
        var child = await api.Anonymous().GetStringAsync("/sitemaps/partners.xml");
        Assert.Contains("<loc>http://app.test/partners</loc>", child);
        Assert.Contains("<loc>http://app.test/partners/pci-ai</loc>", child);
        Assert.Contains("<loc>http://app.test/partners/certuvo</loc>", child);
        Assert.DoesNotContain($"/partners/{hidden}<", child);
        Assert.DoesNotContain($"/partners/{noindex}<", child);
        var sitemap = await (await api.Anonymous().GetAsync("/api/v1/public/sitemap.xml")).Content.ReadAsStringAsync();
        Assert.Contains("<loc>http://app.test/partners</loc>", sitemap);
        Assert.Contains("<loc>http://app.test/partners/pci-ai</loc>", sitemap);
        Assert.Contains("<loc>http://app.test/partners/certuvo</loc>", sitemap);
        Assert.DoesNotContain($"/partners/{hidden}<", sitemap);
        Assert.DoesNotContain($"/partners/{noindex}<", sitemap);
        await api.Anonymous().GetJsonAsync($"{Public}/{hidden}", 404);
        var profile = await api.Anonymous().GetJsonAsync($"{Public}/{noindex}");
        Assert.True(profile.GetProperty("seo").GetProperty("noIndex").GetBoolean());
    }

    // ---------------------------------------------------------------- staff CRUD

    [Fact]
    public async Task Staff_create_edit_and_delete_partners_with_validation_conflicts_and_audit()
    {
        var admin = await api.AdminAsync();
        var slug = Unique("acme");

        // Validation: one 400 with every field error.
        var bad = Partner(slug, "http://acme.example", "home.partners", "no.such.slot", "partners.profile");
        bad["logoUrl"] = "https://evil.example/logo.png";
        bad["brandColor"] = "red";
        bad["utmSource"] = "has spaces";
        bad["categories"] = new[] { "Not A Slug!" };
        bad["offerings"] = new[] { new { title = "Item", link = "https://elsewhere.example" } };
        bad["offerCode"] = "SAVE10";
        bad["offerConfirmed"] = true;
        var problem = await admin.PostJsonAsync(Admin, bad, 400);
        Assert.Equal("website.invalid", problem.Code());
        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "websiteUrl", "logoUrl", "brandColor", "utmSource", "categories", "slots", "offerings[0].link", "offerText", "offerConfirmed" })
            Assert.True(errors.TryGetProperty(field, out _), $"missing error for {field}: {errors}");

        // Without a website the partner is live but has no outbound link at all.
        var created = await admin.PostJsonAsync(Admin, Partner(slug, null, PartnerSlots.HomeStrip, PartnerSlots.BlogEnd), 201);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Optimize All is the official marketing partner of {Partner}", created.GetProperty("relationshipLabel").GetString());
        Assert.Equal("optimizeall", created.GetProperty("utmSource").GetString());
        var card = (await api.Anonymous().GetJsonAsync(Public)).GetProperty("partners").EnumerateArray().Single(p => p.GetProperty("slug").GetString() == slug);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("visitUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("websiteHost").ValueKind);
        Assert.Equal($"Optimize All is the official marketing partner of Partner {slug}", card.GetProperty("relationshipLabel").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Visitor().GetAsync($"{Public}/{slug}/visit?slot=blog.end&path=/blog/x")).StatusCode);
        var profile = await api.Anonymous().GetJsonAsync($"{Public}/{slug}");
        Assert.DoesNotContain("\"url\"", profile.GetProperty("jsonLd")[0].GetRawText().Replace("\"@id\"", ""));

        // Duplicate slug: 409.
        Assert.Equal("website.slug_taken", (await admin.PostJsonAsync(Admin, Partner(slug), 409)).Code());

        // Update with the current stamp; a stale stamp is 409 concurrency.conflict.
        var update = Partner(slug, "https://www.acme.example/start", PartnerSlots.HomeStrip);
        update["concurrencyStamp"] = created.GetProperty("concurrencyStamp").GetGuid();
        update["brandColor"] = "#1f3a93";
        var updated = await admin.PutJsonAsync($"{Admin}/{id}", update);
        Assert.Equal("#1F3A93", updated.GetProperty("brandColor").GetString());
        Assert.Equal(new[] { PartnerSlots.HomeStrip }, updated.GetProperty("slots").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal("concurrency.conflict", (await admin.PutJsonAsync($"{Admin}/{id}", update, 409)).Code());
        card = (await api.Anonymous().GetJsonAsync(Public)).GetProperty("partners").EnumerateArray().Single(p => p.GetProperty("slug").GetString() == slug);
        Assert.Equal("acme.example", card.GetProperty("websiteHost").GetString());

        var listed = await admin.GetJsonAsync(Admin);
        Assert.Contains(listed.EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        var slots = await admin.GetJsonAsync($"{Admin}/slots");
        Assert.Contains(slots.EnumerateArray(), s => s.GetProperty("name").GetString() == PartnerSlots.LearnCourse && s.GetProperty("kind").GetString() == "Unit");

        var response = await admin.DeleteAsync($"{Admin}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await api.Anonymous().GetJsonAsync($"{Public}/{slug}", 404);

        var actions = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().Where(l => l.EntityType == nameof(WebsitePartner) && l.EntityId == id.ToString())
            .Select(l => l.Action).ToListAsync());
        Assert.Equal(new[] { "website.partner_created", "website.partner_deleted", "website.partner_updated" }, actions.Order());
    }

    [Fact]
    public async Task Renaming_an_active_partner_redirects_its_old_profile_and_the_partners_address_is_reserved()
    {
        var admin = await api.AdminAsync();
        var (from, to) = (Unique("old-name"), Unique("new-name"));
        var created = await admin.PostJsonAsync(Admin, Partner(from), 201);
        var rename = Partner(to);
        rename["concurrencyStamp"] = created.GetProperty("concurrencyStamp").GetGuid();
        await admin.PutJsonAsync($"{Admin}/{created.GetProperty("id").GetGuid()}", rename);
        var lookup = await api.Anonymous().GetJsonAsync("/api/v1/public/redirects?path=" + Uri.EscapeDataString($"/partners/{from}"));
        Assert.Equal($"/partners/{to}", lookup.GetProperty("location").GetString());

        var page = await admin.PostJsonAsync("/api/v1/agency/website/pages", new
        {
            slug = "partners", title = "Partners", kind = "Standard", isPublished = false,
            blocks = new[] { new { type = "richText", data = new { markdown = "x" } } },
        }, 400);
        Assert.True(page.GetProperty("errors").TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task Only_site_managers_reach_the_partner_admin()
    {
        var (_, writer) = await api.CreateClientAsync(Role.ContentCreator);
        await (await writer.GetAsync(Admin)).ShouldFailAsync(403);
        await (await writer.PostAsJsonAsync(Admin, Partner(Unique("nope")))).ShouldFailAsync(403);
        await (await writer.GetAsync($"{Admin}/report")).ShouldFailAsync(403);
        await (await writer.GetAsync($"{Admin}/report.csv")).ShouldFailAsync(403);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync($"{Admin}/report")).StatusCode);
    }

    // ---------------------------------------------------------------- targeting

    private async Task<string?> PlacementAsync(string query)
    {
        var body = await api.Anonymous().GetJsonAsync($"{Public}/placement?{query}");
        var partner = body.GetProperty("partner");
        return partner.ValueKind == JsonValueKind.Null ? null : partner.GetProperty("slug").GetString();
    }

    [Fact]
    public async Task An_ad_unit_shows_the_best_matching_partner_and_rotates_when_nothing_matches()
    {
        var admin = await api.AdminAsync();
        var zeta = Unique("zeta");
        await admin.PostJsonAsync(Admin, Partner(zeta, "https://zeta.example", PartnerSlots.BlogEnd), 201);

        // Keywords and categories decide.
        Assert.Equal(zeta, await PlacementAsync($"slot=blog.end&keywords=Quantum%20Widgets%20{zeta}&path=/blog/q"));
        Assert.Equal("pci-ai", await PlacementAsync("slot=blog.end&keywords=earned%20value,forecasting&path=/blog/ev"));
        Assert.Equal("certuvo", await PlacementAsync("slot=blog.end&categories=nursing&path=/blog/nclex"));
        Assert.Equal("certuvo", await PlacementAsync("slot=learn.exam&categories=ai&path=/learn/ai-course/exam"));
        Assert.Equal("pci-ai", await PlacementAsync("slot=careers.index&path=/careers"));

        // Nothing matches: a stable choice per page and day, spread across the eligible partners.
        var first = await PlacementAsync("slot=blog.end&keywords=gardening&path=/blog/roses");
        Assert.NotNull(first);
        Assert.Equal(first, await PlacementAsync("slot=blog.end&keywords=gardening&path=/blog/roses"));
        var seen = new HashSet<string?>();
        for (var i = 0; i < 24; i++) seen.Add(await PlacementAsync($"slot=blog.end&keywords=gardening&path=/blog/post-{i}"));
        Assert.True(seen.Count >= 2, "rotation should spread units across partners");

        // Only Certuvo is enabled on the learner dashboard; unknown and list slots are rejected.
        Assert.Equal("certuvo", await PlacementAsync("slot=learn.dashboard&path=/learn&keywords=" + Unique("x")));
        await (await api.Anonymous().GetAsync($"{Public}/placement?slot=nope")).ShouldFailAsync(400, "website.partner_slot_invalid");
        await (await api.Anonymous().GetAsync($"{Public}/placement?slot=home.partners")).ShouldFailAsync(400, "website.partner_slot_invalid");
    }

    // ---------------------------------------------------------------- tracking, report, export

    private async Task<(int Impressions, int Clicks)> CountsAsync(string slug) => await api.WithDbAsync(async db =>
    {
        var id = await db.Set<WebsitePartner>().Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync();
        var rows = await db.Set<WebsitePartnerStat>().AsNoTracking().Where(s => s.PartnerId == id).ToListAsync();
        return (rows.Sum(r => r.Impressions), rows.Sum(r => r.Clicks));
    });

    [Fact]
    public async Task Impressions_and_clicks_are_counted_per_slot_and_page_without_bots_and_reported()
    {
        var admin = await api.AdminAsync();
        var slug = Unique("tracked");
        var created = await admin.PostJsonAsync(Admin, Partner(slug, "https://tracked.example/?ref=home", PartnerSlots.BlogEnd, PartnerSlots.HomeStrip), 201);
        var id = created.GetProperty("id").GetGuid();

        var batch = new
        {
            items = new object?[]
            {
                new { partner = slug, slot = "blog.end", path = "/blog/Seo-Tips?utm_source=x" },
                new { partner = slug, slot = "blog.end", path = "/blog/seo-tips" }, // same page: counted once per batch
                new { partner = slug, slot = "home.partners", path = "/" },
                new { partner = slug, slot = "learn.exam", path = "/learn/x" }, // not enabled for this slot
                new { partner = slug, slot = "blog.end", path = "//evil.example/<script>" }, // not a public page
                new { partner = "no-such-partner", slot = "blog.end", path = "/blog/x" },
                null,
            },
        };
        var bot = await Visitor("Googlebot/2.1 (+http://www.google.com/bot.html)").PostJsonAsync($"{Public}/impressions", batch, 202);
        Assert.Equal(0, bot.GetProperty("accepted").GetInt32());
        Assert.Equal((0, 0), await CountsAsync(slug));
        var human = await Visitor().PostJsonAsync($"{Public}/impressions", batch, 202);
        Assert.Equal(2, human.GetProperty("accepted").GetInt32());
        await Visitor().PostJsonAsync($"{Public}/impressions", batch, 202);
        Assert.Equal((4, 0), await CountsAsync(slug));

        // The click redirect: 302 to the partner's site with UTM tags (campaign = slot), counted for humans only.
        var click = await Visitor().GetAsync($"{Public}/{slug}/visit?slot=blog.end&path=/blog/seo-tips");
        Assert.Equal(HttpStatusCode.Redirect, click.StatusCode);
        Assert.Equal("https://tracked.example/?ref=home&utm_source=optimizeall&utm_medium=partner&utm_campaign=blog.end",
            click.Headers.Location!.ToString());
        Assert.Contains("noindex", click.Headers.GetValues("X-Robots-Tag").Single());
        var botClick = await Visitor("curl/8.0").GetAsync($"{Public}/{slug}/visit?slot=blog.end&path=/blog/seo-tips");
        Assert.Equal(HttpStatusCode.Redirect, botClick.StatusCode);
        // An unknown slot counts as the profile page; an unusable path redirects without counting.
        var profileClick = await Visitor().GetAsync($"{Public}/{slug}/visit?slot=bogus&path=/partners/{slug}");
        Assert.EndsWith("utm_campaign=partners.profile", profileClick.Headers.Location!.ToString());
        await Visitor().GetAsync($"{Public}/{slug}/visit?slot=blog.end&path=javascript:alert(1)");
        Assert.Equal((4, 2), await CountsAsync(slug));

        var report = await admin.GetJsonAsync($"{Admin}/report?partnerId={id}");
        Assert.Equal(4, report.GetProperty("impressions").GetInt32());
        Assert.Equal(2, report.GetProperty("clicks").GetInt32());
        Assert.Equal(0.5m, report.GetProperty("clickThroughRate").GetDecimal());
        var bySlot = report.GetProperty("bySlot").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!, s => s.GetProperty("impressions").GetInt32());
        Assert.Equal(2, bySlot["blog.end"]);
        Assert.Equal(2, bySlot["home.partners"]);
        Assert.Contains(report.GetProperty("byPage").EnumerateArray(), p => p.GetProperty("key").GetString() == "/blog/seo-tips");
        Assert.Equal(1, report.GetProperty("daily").GetArrayLength());

        var csv = await admin.GetAsync($"{Admin}/report.csv?partnerId={id}&slot=blog.end");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = (await csv.Content.ReadAsStringAsync()).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("day,partner,partner_name,slot,page,impressions,clicks,ctr", lines[0]);
        Assert.Contains(lines, l => l.Contains($",{slug},") && l.Contains(",blog.end,/blog/seo-tips,2,1,0.5"));
        Assert.DoesNotContain(lines, l => l.Contains("home.partners"));

        await (await admin.GetAsync($"{Admin}/report?from=2026-05-01&to=2026-04-01")).ShouldFailAsync(400, "website.invalid_range");
        await (await admin.GetAsync($"{Admin}/report?from=0001-01-01")).ShouldFailAsync(400); // the global date guard answers first
    }

    // ---------------------------------------------------------------- offers

    [Fact]
    public async Task An_offer_is_shown_only_when_confirmed_and_until_it_expires_or_30_days_after_its_last_edit()
    {
        var admin = await api.AdminAsync();
        var slug = Unique("offer");
        var input = Partner(slug, "https://offer.example");
        input["offerText"] = "20% off the first month";
        input["offerCode"] = "WELCOME20";
        var created = await admin.PostJsonAsync(Admin, input, 201);
        Assert.False(created.GetProperty("offerVisible").GetBoolean());
        async Task<JsonElement> OfferAsync() => (await api.Anonymous().GetJsonAsync($"{Public}/{slug}")).GetProperty("offer");
        Assert.Equal(JsonValueKind.Null, (await OfferAsync()).ValueKind);

        input["offerConfirmed"] = true;
        input["concurrencyStamp"] = created.GetProperty("concurrencyStamp").GetGuid();
        var confirmed = await admin.PutJsonAsync($"{Admin}/{created.GetProperty("id").GetGuid()}", input);
        Assert.True(confirmed.GetProperty("offerVisible").GetBoolean());
        Assert.Equal("WELCOME20", (await OfferAsync()).GetProperty("code").GetString());

        api.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(JsonValueKind.Null, (await OfferAsync()).ValueKind);

        // With an expiry date: shown until that instant, whatever the age of the edit. (Sessions expire as the clock moves.)
        input["offerExpiresAt"] = api.Clock.GetUtcNow().UtcDateTime.AddDays(60);
        input["concurrencyStamp"] = confirmed.GetProperty("concurrencyStamp").GetGuid();
        var dated = await (await api.AdminAsync()).PutJsonAsync($"{Admin}/{created.GetProperty("id").GetGuid()}", input);
        api.Clock.Advance(TimeSpan.FromDays(45));
        Assert.Equal("20% off the first month", (await OfferAsync()).GetProperty("text").GetString());
        api.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Equal(JsonValueKind.Null, (await OfferAsync()).ValueKind);
        Assert.False((await (await api.AdminAsync()).GetJsonAsync($"{Admin}/{dated.GetProperty("id").GetGuid()}")).GetProperty("offerVisible").GetBoolean());
    }

    // ---------------------------------------------------------------- server-rendered pages (technical SEO)

    [Fact]
    public async Task The_server_renders_the_partner_pages_with_their_seo_and_json_ld_and_sponsored_links()
    {
        var web = api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var directory = await web.GetAsync("/_document/partners");
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        var dir = await directory.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Our partners</h1>", dir);
        Assert.Contains("<link rel=\"canonical\" href=\"http://app.test/partners\"", dir);
        Assert.Contains("\"@type\":\"CollectionPage\"", dir);
        Assert.Contains("\"@type\":\"ItemList\"", dir);
        Assert.Contains("href=\"/partners/pci-ai\"", dir);

        var profile = await web.GetAsync("/_document/partners/pci-ai");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var html = await profile.Content.ReadAsStringAsync();
        Assert.Contains("<link rel=\"canonical\" href=\"http://app.test/partners/pci-ai\"", html);
        Assert.Contains("\"@type\":\"Organization\"", html);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", html);
        Assert.Matches("<h1>[^<]+</h1>", html);
        // The click counter link is sponsored; no link to a partner's site is left unqualified.
        Assert.Matches("<a href=\"/api/v1/public/partners/pci-ai/visit\\?[^\"]*\" rel=\"sponsored noopener\" target=\"_blank\">", html);
        foreach (System.Text.RegularExpressions.Match a in System.Text.RegularExpressions.Regex.Matches(html, "<a [^>]*href=\"(https?://(www\\.)?(pciai\\.org|certuvo\\.com)[^\"]*)\"[^>]*>"))
        {
            Assert.Contains("rel=\"sponsored noopener\"", a.Value);
            Assert.Contains("utm_source=", a.Groups[1].Value);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await web.GetAsync("/_document/partners/no-such-partner")).StatusCode);

        // llms.txt lists the partner pages; the admin SEO overview shows every active profile.
        var llms = await api.Anonymous().GetStringAsync("/llms.txt");
        Assert.Contains("## Partners", llms);
        Assert.Contains("/partners/pci-ai.md", llms);
        var overview = await (await api.AdminAsync()).GetJsonAsync("/api/v1/agency/website/seo/overview");
        var paths = overview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("path").GetString()).ToList();
        Assert.Contains("/partners", paths);
        Assert.Contains("/partners/pci-ai", paths);
        Assert.Contains("/partners/certuvo", paths);
    }

    [Fact]
    public async Task Links_to_a_partner_in_server_rendered_content_are_sponsored_with_utm_tags()
    {
        var admin = await api.AdminAsync();
        var slug = Unique("partner-link-post");
        var body = "Certified by an independent body: see [the Certuvo catalogue](https://www.certuvo.com/catalogue) and " +
                   "[our services](/services). " + string.Join(" ", Enumerable.Repeat("Long enough to publish as a real post body.", 4));
        var post = await admin.PostJsonAsync("/api/v1/agency/website/blog/posts", new { slug, title = "Partner link post", excerpt = "Excerpt.", bodyMarkdown = body }, 201);
        await admin.PostJsonAsync($"/api/v1/agency/website/blog/posts/{post.GetProperty("id").GetGuid()}/publish",
            new { concurrencyStamp = post.GetProperty("concurrencyStamp").GetGuid() }, 200);

        var html = await (await api.CreateClient().GetAsync($"/_document/blog/{slug}")).Content.ReadAsStringAsync();
        var anchor = System.Text.RegularExpressions.Regex.Match(html, "<a [^>]*href=\"https://www\\.certuvo\\.com/catalogue[^\"]*\"[^>]*>");
        Assert.True(anchor.Success, "the partner link is rendered");
        Assert.Contains("rel=\"sponsored noopener\"", anchor.Value);
        Assert.Contains("target=\"_blank\"", anchor.Value);
        Assert.Contains("utm_source=", anchor.Value);
        Assert.Contains("utm_campaign=editorial", anchor.Value);
        // Internal links are untouched.
        Assert.Contains("<a href=\"/services\">our services</a>", html);
    }
}
