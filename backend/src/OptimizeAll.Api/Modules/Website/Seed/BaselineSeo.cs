using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Seed;

/// <summary>
/// Search titles and descriptions for the baseline CMS pages (docs/SEO_CRO.md § Metadata): titles of at most 45
/// characters (60 with the " | Optimize All" suffix) and descriptions of 70–155 characters. Editors change them in
/// Pages → SEO; the seed never overwrites a page that exists.
/// </summary>
internal static class BaselineSeo
{
    private static readonly Dictionary<string, (string Title, string Description)> Pages = new(StringComparer.Ordinal)
    {
        ["about"] = ("About Us: The Academy and the Agency",
            "Optimize All helps people and businesses grow: a free academy with certificate-backed courses, and a full-service marketing agency with honest reporting."),
        ["academy"] = ("Academy: Free AI and Marketing Courses",
            "Free, self-paced courses in AI, marketing, SEO, sales, design and business. Pass the assessment to earn a verifiable certificate you can add to LinkedIn."),
        ["how-we-work"] = ("How We Work: Audit, Strategy, Execution",
            "Our four-step process: an honest audit, a prioritised 90-day strategy, specialist execution you approve, and clear monthly reporting on results."),
        ["privacy-policy"] = ("Privacy Policy: How We Protect Your Data",
            "How Optimize All collects, uses, stores and protects personal data on this website and in our services, and how to exercise your privacy rights."),
        ["terms-of-service"] = ("Terms of Service for Our Website and Work",
            "The terms that apply when you use the Optimize All website, send us a form, book a consultation or buy marketing services from our agency."),
        ["cookie-policy"] = ("Cookie Policy and Your Privacy Choices",
            "Which cookies the Optimize All website uses and why, and how to accept, reject or change your analytics and marketing cookie choices at any time."),
        ["accessibility"] = ("Accessibility Statement and WCAG 2.2 Goals",
            "Our commitment to an accessible website for everyone: the standards we follow, known limitations and how to report an accessibility problem."),
        ["refund-policy"] = ("Refund Policy: Cancellations and Refunds",
            "How cancellations, refunds and notice periods work for Optimize All retainers, one-time projects and consultations, and how to ask for a refund."),
    };

    /// <summary>Services whose hero copy opens with a sentence too long for a search snippet.</summary>
    private static readonly Dictionary<string, string> ServiceDescriptions = new(StringComparer.Ordinal)
    {
        ["influencer-ugc-marketing"] =
            "Influencer and UGC campaigns through our creator network: vetted, established accounts share your approved content; every post is reviewed and disclosed.",
    };

    /// <summary>
    /// A search description from longer copy: whole sentences up to 155 characters (never a cut-off sentence); the
    /// first sentence alone, cut at a word boundary, when even that is longer.
    /// </summary>
    public static string Description(string text, string? serviceSlug = null)
    {
        if (serviceSlug is not null && ServiceDescriptions.TryGetValue(serviceSlug, out var written)) return written;
        var sentences = System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[.!?])\s+");
        var result = string.Empty;
        foreach (var sentence in sentences)
        {
            var next = result.Length == 0 ? sentence : result + " " + sentence;
            if (next.Length > SiteSeo.SeoText.DescriptionMax) break;
            result = next;
        }
        return result.Length > 0 ? result : SiteSeo.SeoText.Clamp(sentences[0])!;
    }

    /// <summary>The SEO fields of a baseline page (the summary as description when no copy is defined).</summary>
    /// <summary>The About page's search title and description before the two-pillar repositioning (2026-09).</summary>
    public const string PreviousAboutTitle = "About Optimize All: Our Story and Values";

    public const string PreviousAboutDescription =
        "Optimize All is a full-service digital marketing agency built on measurable growth: senior strategists, specialist teams and honest reporting.";

    public static SeoMeta ForPage(string slug, string summary) =>
        Pages.TryGetValue(slug, out var seo) ? new SeoMeta { Title = seo.Title, Description = seo.Description } : new SeoMeta { Description = summary };
}
