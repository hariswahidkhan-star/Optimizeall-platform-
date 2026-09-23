using System.Text;

namespace OptimizeAll.Domain.Seo;

public sealed record ChecklistItem(string Key, string Title, string Guidance);

public sealed record DirectoryDefinition(string Key, string Name, string Url, string Category, string[] Countries);

public sealed record NapComparison(bool? NameMatches, bool? AddressMatches, bool? PhoneMatches)
{
    public bool Consistent => NameMatches != false && AddressMatches != false && PhoneMatches != false;
}

/// <summary>Local SEO reference data: Google Business Profile checklist, citation directories and NAP matching.</summary>
public static class LocalSeoCatalog
{
    public static readonly IReadOnlyList<ChecklistItem> GbpChecklist = new ChecklistItem[]
    {
        new("gbp.claimed", "Profile claimed and verified", "Claim the Business Profile and complete verification (postcard, phone, email or video) so you control the listing."),
        new("gbp.name", "Business name matches real-world branding", "Use the exact name on your signage and website; keyword stuffing the name risks suspension."),
        new("gbp.primary_category", "Most specific primary category", "Pick the narrowest primary category that describes the core business; it is the strongest local relevance signal."),
        new("gbp.secondary_categories", "Relevant secondary categories", "Add secondary categories for other services you genuinely offer."),
        new("gbp.nap", "Address and phone match the website", "Use the same name, address and phone (NAP) as the website footer and major citations."),
        new("gbp.hours", "Regular and holiday hours", "Keep opening hours current, including special hours for holidays, so Google does not show 'hours might differ'."),
        new("gbp.website", "Website link with UTM tracking", "Link to the most relevant landing page and tag it with UTM parameters to measure Business Profile traffic."),
        new("gbp.description", "Keyword-rich business description", "Write a 750-character description covering services, service area and what makes the business different."),
        new("gbp.services", "Services or products listed", "Add every service or product with a short description and price where appropriate."),
        new("gbp.photos", "At least 10 recent photos", "Upload a logo, cover photo, exterior, interior, team and product photos; refresh them monthly."),
        new("gbp.attributes", "Attributes completed", "Set attributes such as accessibility, payment options and amenities that apply."),
        new("gbp.posts", "Weekly Google posts", "Publish updates, offers or events at least weekly to show activity and promote conversions."),
        new("gbp.qa", "Q&A seeded and monitored", "Add frequently asked questions with answers and monitor new questions from the public."),
        new("gbp.reviews_strategy", "Review generation process", "Ask every happy customer for a review with a short direct link; never incentivize reviews."),
        new("gbp.review_responses", "Responding to every review", "Reply to all reviews within 48 hours — thank positive reviewers and resolve negative ones publicly and politely."),
        new("gbp.messaging", "Messaging or booking enabled", "Enable chat or a booking link where available so searchers can convert straight from the profile."),
        new("gbp.service_area", "Service area defined (service-area businesses)", "Set service areas by city or region if you serve customers at their location, and hide the address if needed."),
        new("gbp.insights", "Monthly performance review", "Review calls, direction requests, website clicks and search terms monthly and adjust categories and posts."),
    };

    /// <summary>Thirty widely used citation sources (general, maps, social, review and regional directories).</summary>
    public static readonly IReadOnlyList<DirectoryDefinition> Directories = new DirectoryDefinition[]
    {
        new("google-business", "Google Business Profile", "https://business.google.com", "Maps & search", Array.Empty<string>()),
        new("bing-places", "Bing Places for Business", "https://www.bingplaces.com", "Maps & search", Array.Empty<string>()),
        new("apple-business-connect", "Apple Business Connect", "https://businessconnect.apple.com", "Maps & search", Array.Empty<string>()),
        new("facebook", "Facebook Page", "https://www.facebook.com", "Social", Array.Empty<string>()),
        new("instagram", "Instagram Business", "https://www.instagram.com", "Social", Array.Empty<string>()),
        new("linkedin", "LinkedIn Company Page", "https://www.linkedin.com", "Social", Array.Empty<string>()),
        new("yelp", "Yelp", "https://biz.yelp.com", "Reviews", new[] { "US", "GB", "CA", "AU" }),
        new("tripadvisor", "Tripadvisor", "https://www.tripadvisor.com", "Reviews", Array.Empty<string>()),
        new("trustpilot", "Trustpilot", "https://business.trustpilot.com", "Reviews", Array.Empty<string>()),
        new("foursquare", "Foursquare", "https://foursquare.com", "Data aggregator", Array.Empty<string>()),
        new("here", "HERE WeGo", "https://www.here.com", "Maps & search", Array.Empty<string>()),
        new("tomtom", "TomTom", "https://www.tomtom.com", "Maps & search", Array.Empty<string>()),
        new("openstreetmap", "OpenStreetMap", "https://www.openstreetmap.org", "Maps & search", Array.Empty<string>()),
        new("yellow-pages", "Yellow Pages", "https://www.yellowpages.com", "General directory", new[] { "US", "CA" }),
        new("better-business-bureau", "Better Business Bureau", "https://www.bbb.org", "General directory", new[] { "US", "CA" }),
        new("manta", "Manta", "https://www.manta.com", "General directory", new[] { "US" }),
        new("angi", "Angi", "https://www.angi.com", "Home services", new[] { "US" }),
        new("nextdoor", "Nextdoor", "https://business.nextdoor.com", "Community", new[] { "US", "GB" }),
        new("yell", "Yell", "https://www.yell.com", "General directory", new[] { "GB" }),
        new("thomson-local", "Thomson Local", "https://www.thomsonlocal.com", "General directory", new[] { "GB" }),
        new("scoot", "Scoot", "https://www.scoot.co.uk", "General directory", new[] { "GB" }),
        new("yellow-pages-uae", "Yellow Pages UAE", "https://www.yellowpages-uae.com", "General directory", new[] { "AE" }),
        new("connect-ae", "Connect.ae", "https://www.connect.ae", "General directory", new[] { "AE" }),
        new("zomato", "Zomato", "https://www.zomato.com", "Restaurants", new[] { "AE", "IN" }),
        new("foodpanda", "foodpanda", "https://www.foodpanda.pk", "Restaurants", new[] { "PK" }),
        new("olx-pakistan", "OLX Pakistan", "https://www.olx.com.pk", "Classifieds", new[] { "PK" }),
        new("pakbiz", "Pakbiz", "https://www.pakbiz.com", "General directory", new[] { "PK" }),
        new("hotfrog", "Hotfrog", "https://www.hotfrog.com", "General directory", Array.Empty<string>()),
        new("cylex", "Cylex", "https://www.cylex.net", "General directory", Array.Empty<string>()),
        new("brownbook", "Brownbook", "https://www.brownbook.net", "General directory", Array.Empty<string>()),
    };

    /// <summary>Compares a listing against the canonical NAP. Null = not provided on the listing.</summary>
    public static NapComparison Compare(string? canonicalName, string? canonicalAddress, string? canonicalPhone,
        string? listedName, string? listedAddress, string? listedPhone) => new(
        Compare(canonicalName, listedName, NormalizeName),
        Compare(canonicalAddress, listedAddress, NormalizeAddress),
        Compare(canonicalPhone, listedPhone, NormalizePhone));

    private static bool? Compare(string? canonical, string? listed, Func<string, string> normalize) =>
        string.IsNullOrWhiteSpace(listed) || string.IsNullOrWhiteSpace(canonical) ? null : normalize(canonical) == normalize(listed);

    public static string NormalizeName(string value) => Alphanumeric(value.Replace("&", " and "));

    private static readonly (string From, string To)[] AddressAbbreviations =
    {
        ("street", "st"), ("road", "rd"), ("avenue", "ave"), ("boulevard", "blvd"), ("suite", "ste"), ("floor", "fl"),
        ("building", "bldg"), ("drive", "dr"), ("lane", "ln"), ("north", "n"), ("south", "s"), ("east", "e"), ("west", "w"),
    };

    public static string NormalizeAddress(string value)
    {
        var words = value.ToLowerInvariant().Split(new[] { ' ', ',', '.', '\n', '\r', '\t', '#', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => AddressAbbreviations.FirstOrDefault(a => a.From == w).To ?? w);
        return string.Join(' ', words);
    }

    /// <summary>Digits only, compared on the last 9 digits so "+44 20 7946 0000" and "020 7946 0000" match.</summary>
    public static string NormalizePhone(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 9 ? digits[^9..] : digits;
    }

    private static string Alphanumeric(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
