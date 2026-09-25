using OptimizeAll.Api.Modules.Website.Seed;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Website;
using Xunit;

namespace OptimizeAll.UnitTests.Website;

/// <summary>
/// The two-pillar repositioning (academy + agency): new defaults put the academy first, and stored settings are upgraded
/// only where they still equal the previous built-in defaults — an administrator's own edits are never replaced.
/// </summary>
public class TwoPillarDefaultsTests
{
    private static SiteSettings Previous() => SiteSettingsService.Defaults with
    {
        Header = new HeaderSettings(SiteSettingsService.PreviousDefaults.Menu, SiteSettingsService.PreviousDefaults.Cta),
        Footer = SiteSettingsService.Defaults.Footer with
        {
            Blurb = SiteSettingsService.PreviousDefaults.Blurb,
            Columns = SiteSettingsService.PreviousDefaults.Columns,
        },
        Seo = SiteSettingsService.Defaults.Seo with
        {
            DefaultTitle = SiteSettingsService.PreviousDefaults.SeoTitle,
            DefaultDescription = SiteSettingsService.PreviousDefaults.SeoDescription,
        },
    };

    [Fact]
    public void Defaults_lead_with_the_academy_and_keep_both_pillars_one_click_away()
    {
        var d = SiteSettingsService.Defaults;
        Assert.Equal("Academy", d.Header.Menu[0].Label);
        Assert.Equal("/academy", d.Header.Menu[0].Url);
        Assert.Contains(d.Header.Menu[0].Children!, c => c.Url == "/learn");
        Assert.Contains(d.Header.Menu, m => m.Url == "/services");
        Assert.Contains(d.Header.Menu, m => m.Label == "Pricing" && m.Url == "/pricing");
        Assert.Equal("/learn", d.Header.Cta!.Url);
        Assert.Equal("Academy", d.Footer.Columns[0].Title);
        Assert.InRange(d.Seo.DefaultTitle.Length, 30, 60);
        Assert.InRange(d.Seo.DefaultDescription!.Length, 70, 155);
    }

    [Fact]
    public void Untouched_previous_defaults_are_upgraded()
    {
        var upgraded = SiteSettingsService.UpgradeFromPreviousDefaults(Previous());
        Assert.NotNull(upgraded);
        Assert.Equal(SiteSettingsService.Defaults.Header.Menu.Select(m => m.Label), upgraded!.Header.Menu.Select(m => m.Label));
        Assert.Equal(SiteSettingsService.Defaults.Header.Cta, upgraded.Header.Cta);
        Assert.Equal(SiteSettingsService.Defaults.Footer.Blurb, upgraded.Footer.Blurb);
        Assert.Equal(SiteSettingsService.Defaults.Footer.Columns.Select(c => c.Title), upgraded.Footer.Columns.Select(c => c.Title));
        Assert.Equal(SiteSettingsService.Defaults.Seo.DefaultTitle, upgraded.Seo.DefaultTitle);
    }

    [Fact]
    public void A_menu_saved_in_the_admin_without_changes_still_counts_as_untouched()
    {
        // Saving turns "no sub-items" (null) into an empty list.
        var saved = Previous() with
        {
            Header = new HeaderSettings(
                SiteSettingsService.PreviousDefaults.Menu.Select(m => m with { Children = m.Children ?? Array.Empty<MenuItem>() }).ToList(),
                SiteSettingsService.PreviousDefaults.Cta),
        };
        Assert.Equal("Academy", SiteSettingsService.UpgradeFromPreviousDefaults(saved)!.Header.Menu[0].Label);
    }

    [Fact]
    public void Edited_parts_are_kept_and_current_settings_are_left_alone()
    {
        var edited = Previous() with
        {
            Header = new HeaderSettings(new[] { new MenuItem("Shop", "/shop", null, null) }, new SiteLink("Buy", "/shop")),
            Footer = Previous().Footer with { Blurb = "Our own words." },
        };
        var upgraded = SiteSettingsService.UpgradeFromPreviousDefaults(edited)!;
        Assert.Equal("Shop", Assert.Single(upgraded.Header.Menu).Label);
        Assert.Equal("Buy", upgraded.Header.Cta!.Label);
        Assert.Equal("Our own words.", upgraded.Footer.Blurb);
        Assert.Equal("Academy", upgraded.Footer.Columns[0].Title); // the untouched columns still move to the new defaults

        Assert.Null(SiteSettingsService.UpgradeFromPreviousDefaults(SiteSettingsService.Defaults));
    }

    [Fact]
    public void The_academy_page_ships_with_the_about_page_and_search_snippets_within_limits()
    {
        var academy = Assert.Single(BaselinePages.Pages, p => p.Slug == "academy");
        Assert.Equal("hero", academy.Blocks[0].Type);
        Assert.NotEqual(BaselinePages.PreviousAbout.Summary, BaselinePages.Pages.Single(p => p.Slug == "about").Summary);
        var seo = BaselineSeo.ForPage("academy", academy.Summary);
        Assert.InRange((seo.Title + " | Optimize All").Length, 30, 60);
    }
}
