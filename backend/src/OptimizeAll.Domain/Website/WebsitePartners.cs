using System.Text.RegularExpressions;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

/// <summary>
/// A partner organization Optimize All promotes on its public website (docs/WEBSITE.md "Partners and sponsored
/// placements"): a profile page at <c>/partners/{slug}</c>, a card on <c>/partners</c>, and ad units in the placement
/// slots it is enabled for (<see cref="PartnerSlots"/>). Every outbound link to the partner is a paid/partnership link:
/// it is rendered with <c>rel="sponsored noopener"</c>, goes through the click counter and carries the UTM tags below.
/// </summary>
public class WebsitePartner : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Uploaded image (/api/v1/files/…), an allow-listed https URL, or a bundled asset under /partners/.</summary>
    public string LogoUrl { get; set; } = string.Empty;

    /// <summary>The partner's own site. While empty, every link and call to action to the partner is hidden.</summary>
    public string? WebsiteUrl { get; set; }

    public string Tagline { get; set; } = string.Empty;
    public string? DescriptionMarkdown { get; set; }

    /// <summary>The visible statement of the relationship, e.g. "Optimize All is the official marketing partner of {Partner}".</summary>
    public string RelationshipLabel { get; set; } = string.Empty;

    /// <summary>Short facts shown as a checklist on the profile page ("Fully online · scenario-based exam").</summary>
    public List<string> Highlights { get; set; } = new();

    /// <summary>Products, programmes or certifications, one section each on the profile page.</summary>
    public List<PartnerOffering> Offerings { get; set; } = new();

    /// <summary>Topics used for targeting (matched against page keywords) and as schema.org <c>knowsAbout</c>.</summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>Page categories the partner targets (blog category slugs, service slugs, course categories).</summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>The partner's official profiles (schema.org <c>sameAs</c>).</summary>
    public List<string> SameAs { get; set; } = new();

    /// <summary>Other partners shown as "Related partners" (internal links between profile pages).</summary>
    public List<Guid> RelatedPartnerIds { get; set; } = new();

    /// <summary>Placement slots (<see cref="PartnerSlots"/>) the partner's ad units may appear in.</summary>
    public List<string> Slots { get; set; } = new();

    /// <summary>#rrggbb accent for the partner's cards.</summary>
    public string? BrandColor { get; set; }

    public string UtmSource { get; set; } = "optimizeall";
    public string UtmMedium { get; set; } = "partner";

    /// <summary>utm_campaign; when empty the slot name is used, so each placement is measurable in the partner's analytics.</summary>
    public string? UtmCampaign { get; set; }

    /// <summary>A current promotion of the partner ("50% off your first year"). See <see cref="PartnerOfferRules"/>.</summary>
    public string? OfferText { get; set; }

    public string? OfferCode { get; set; }
    public DateTime? OfferExpiresAt { get; set; }

    /// <summary>An editor confirmed the offer is valid. Until then it is never shown (seeded offers start unconfirmed).</summary>
    public bool OfferConfirmed { get; set; }

    /// <summary>When the offer text, code, expiry or confirmation last changed (the 30-day rule for offers without an expiry).</summary>
    public DateTime? OfferUpdatedAt { get; set; }

    public SeoMeta Seo { get; set; } = new();
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>
/// When a partner's offer is shown: it has a text, an editor confirmed it, and it has not expired — an offer with an
/// expiry date is shown until that instant; one without is shown for 30 days after it was last edited or confirmed, so a
/// forgotten promotion never stays on the site indefinitely.
/// </summary>
public static class PartnerOfferRules
{
    public static readonly TimeSpan UndatedLifetime = TimeSpan.FromDays(30);

    public static bool IsVisible(WebsitePartner p, DateTime now) =>
        !string.IsNullOrWhiteSpace(p.OfferText) && p.OfferConfirmed &&
        (p.OfferExpiresAt is { } expires ? expires > now : p.OfferUpdatedAt is { } edited && edited + UndatedLifetime > now);

    /// <summary>The instant the offer stops being shown (null: not shown at all).</summary>
    public static DateTime? VisibleUntil(WebsitePartner p) =>
        string.IsNullOrWhiteSpace(p.OfferText) || !p.OfferConfirmed ? null
        : p.OfferExpiresAt ?? (p.OfferUpdatedAt is { } edited ? edited + UndatedLifetime : null);
}

/// <summary>
/// A product, programme, certification or exam of a partner. <see cref="Anchor"/> is the section id on the profile page
/// (/partners/{slug}#{anchor}); <see cref="Link"/> an optional site path the item links to (e.g. another partner's section).
/// </summary>
public sealed record PartnerOffering(string Title, string? Summary, List<string> Facts, string? Anchor = null, string? Link = null);

/// <summary>
/// Daily impression and click counters of one partner in one slot on one page (the report and CSV export). Bots are
/// never counted. Written only under the <c>website-partner-stats</c> named lock.
/// </summary>
public class WebsitePartnerStat : Entity
{
    public Guid PartnerId { get; set; }
    public string Slot { get; set; } = string.Empty;
    public string PagePath { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public int Impressions { get; set; }
    public int Clicks { get; set; }
}

public enum PartnerSlotKind
{
    /// <summary>Lists every eligible partner (logo strip, footer line).</summary>
    List,

    /// <summary>One ad unit: at most one partner per slot and page view.</summary>
    Unit,

    /// <summary>A partner page itself (profile, directory): counts clicks only; not switchable per partner.</summary>
    Page,
}

public sealed record PartnerSlot(string Name, PartnerSlotKind Kind, string Label, string Description);

/// <summary>
/// The named placements of the public website. The web app renders them with <c>&lt;PartnerSlot slot="…"&gt;</c>
/// (frontend/src/features/public/partners/slots.ts mirrors this list; a unit test keeps them equal).
/// </summary>
public static class PartnerSlots
{
    public const string HomeStrip = "home.partners";
    public const string Footer = "footer.partners";
    public const string BlogInline = "blog.inline";
    public const string BlogEnd = "blog.end";
    public const string ServiceDetail = "service.detail";
    public const string CaseStudyDetail = "case-study.detail";
    public const string Careers = "careers.index";
    public const string LearnCourse = "learn.course";
    public const string LearnLesson = "learn.lesson";
    public const string LearnExam = "learn.exam";
    public const string LearnCertificate = "learn.certificate";
    public const string LearnDashboard = "learn.dashboard";
    public const string Profile = "partners.profile";
    public const string Directory = "partners.directory";

    public static readonly IReadOnlyList<PartnerSlot> All = new PartnerSlot[]
    {
        new(HomeStrip, PartnerSlotKind.List, "Home page — partner strip", "Logo strip \"Official marketing partner of\" on the home page."),
        new(Footer, PartnerSlotKind.List, "Footer — partner line", "\"Optimize All is the official marketing partner of …\" in the footer of every public page."),
        new(BlogInline, PartnerSlotKind.Unit, "Blog post — inline", "One unit inside the article, before its second section."),
        new(BlogEnd, PartnerSlotKind.Unit, "Blog post — end of article", "One unit after the article body."),
        new(ServiceDetail, PartnerSlotKind.Unit, "Service pages", "One unit on each service page, after the overview."),
        new(CaseStudyDetail, PartnerSlotKind.Unit, "Case studies", "One unit on each case study, after the story."),
        new(Careers, PartnerSlotKind.Unit, "Careers page", "One unit on the careers page."),
        new(LearnCourse, PartnerSlotKind.Unit, "Academy — course page", "One unit on academy course pages (targets the course category)."),
        new(LearnLesson, PartnerSlotKind.Unit, "Academy — lesson page", "One unit on academy lesson pages."),
        new(LearnExam, PartnerSlotKind.Unit, "Academy — exam page", "One unit on academy course exam pages."),
        new(LearnCertificate, PartnerSlotKind.Unit, "Academy — certificate pages", "One unit on certificate and verification pages."),
        new(LearnDashboard, PartnerSlotKind.Unit, "Academy — My learning", "One unit on the learner's dashboard."),
        new(Profile, PartnerSlotKind.Page, "Partner profile page", "Calls to action on /partners/{slug}."),
        new(Directory, PartnerSlotKind.Page, "Partners page", "Calls to action on /partners."),
    };

    private static readonly Dictionary<string, PartnerSlot> ByName = All.ToDictionary(s => s.Name, StringComparer.Ordinal);

    public static PartnerSlot? Find(string? name) => name is not null && ByName.TryGetValue(name, out var slot) ? slot : null;

    /// <summary>Slots an editor switches per partner (lists and units).</summary>
    public static IEnumerable<PartnerSlot> Switchable => All.Where(s => s.Kind != PartnerSlotKind.Page);
}

/// <summary>A partner as the targeting sees it.</summary>
public sealed record PartnerCandidate(Guid Id, string Slug, int SortOrder, IReadOnlyList<string> Keywords, IReadOnlyList<string> Categories);

/// <summary>
/// Chooses the partner for an ad unit (docs/WEBSITE.md "Targeting"). Each eligible partner (active, enabled for the slot)
/// scores one point per page keyword that matches one of its keywords and two per page category that equals one of its
/// categories (case- and punctuation-insensitive; a keyword matches when one phrase contains the other as whole
/// words). The best score wins; ties — including "nothing matched", the fallback — rotate deterministically by the
/// rotation key (the page path) and the UTC day, so a page shows a stable unit for a day and different pages spread
/// across partners.
/// </summary>
public static partial class PartnerTargeting
{
    public const int MaxTerms = 30;

    public static PartnerCandidate? Choose(
        IReadOnlyList<PartnerCandidate> eligible, IEnumerable<string>? pageKeywords, IEnumerable<string>? pageCategories, string rotationKey, DateOnly day)
    {
        if (eligible.Count == 0) return null;
        var keywords = Normalize(pageKeywords);
        var categories = Normalize(pageCategories);
        var scored = eligible
            .Select(p => (Partner: p, Score: Score(p, keywords, categories)))
            .ToList();
        var best = scored.Max(s => s.Score);
        var top = scored.Where(s => s.Score == best).Select(s => s.Partner).OrderBy(p => p.SortOrder).ThenBy(p => p.Slug, StringComparer.Ordinal).ToList();
        return top[(int)(Rotation(rotationKey, day) % (uint)top.Count)];
    }

    public static int Score(PartnerCandidate partner, IReadOnlyList<string> pageKeywords, IReadOnlyList<string> pageCategories)
    {
        var score = 0;
        var partnerKeywords = Normalize(partner.Keywords.Concat(partner.Categories));
        foreach (var keyword in pageKeywords)
            if (partnerKeywords.Any(k => PhraseMatch(k, keyword))) score += 1;
        var partnerCategories = Normalize(partner.Categories);
        foreach (var category in pageCategories)
            if (partnerCategories.Contains(category)) score += 2;
        return score;
    }

    /// <summary>Lower-case words separated by single spaces ("AI-Era Certification!" → "ai era certification").</summary>
    public static string NormalizeTerm(string value) =>
        NonWord().Replace(value.ToLowerInvariant(), " ").Trim();

    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? values) =>
        (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Take(MaxTerms)
            .Select(v => NormalizeTerm(v!.Length > 100 ? v[..100] : v)).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>True when one normalized phrase equals the other or contains it as whole words.</summary>
    public static bool PhraseMatch(string a, string b) =>
        a == b || (" " + a + " ").Contains(" " + b + " ", StringComparison.Ordinal) || (" " + b + " ").Contains(" " + a + " ", StringComparison.Ordinal);

    /// <summary>FNV-1a over "key|yyyy-MM-dd": stable across processes (unlike string.GetHashCode).</summary>
    public static uint Rotation(string key, DateOnly day)
    {
        var hash = 2166136261u;
        foreach (var c in $"{key}|{day:yyyy-MM-dd}")
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();
}
