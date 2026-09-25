using System.Text.RegularExpressions;

namespace OptimizeAll.Api.Modules.Website.SiteSeo.SocialCards;

/// <summary>
/// Derives a page's social card from what the server renders for it: the h1 as the title, the content type (and
/// category) as the eyebrow, the meta line under the h1 of courses, lessons and articles as the facts. A page builder
/// that knows better sets <see cref="SeoPage.Card"/> instead.
/// </summary>
public static partial class SocialCardFactory
{
    /// <summary>Where generated cards are served: <c>/og{path}.png</c> (<c>/og/index.png</c> for the home page).</summary>
    public const string Prefix = "/og";

    public static string CardPath(string pagePath) => Prefix + (pagePath == "/" ? "/index" : pagePath) + ".png";

    /// <summary>The page path a card path stands for (null when it is not a card path).</summary>
    public static string? PagePath(string cardPath)
    {
        if (!cardPath.StartsWith(Prefix + "/", StringComparison.Ordinal) || !cardPath.EndsWith(".png", StringComparison.Ordinal)) return null;
        var p = cardPath[Prefix.Length..^4];
        return p == "/index" ? "/" : p.Length > 1 ? p : null;
    }

    /// <summary>True when the page has no social image of its own (none, the built-in/site default, or an SVG that social networks cannot show).</summary>
    public static bool NeedsCard(SeoPage page, IEnumerable<string> defaultImages)
    {
        if (page.OgImage is null) return true;
        var url = page.OgImage.Split('?', '#')[0];
        return url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || defaultImages.Contains(page.OgImage, StringComparer.Ordinal);
    }

    public static SocialCard From(SeoPage page, string siteName)
    {
        var h1 = page.Content.OfType<HeadingNode>().FirstOrDefault(h => h.Level == 1)?.Text;
        var title = !string.IsNullOrWhiteSpace(h1) ? h1 : StripSite(page.Title, siteName);
        var paragraphs = page.Content.OfType<ParagraphNode>().Select(p => p.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var metaLine = MetaLineAfterH1(page);
        var crumbs = page.Breadcrumbs;
        string Parent() => crumbs.Count >= 3 ? crumbs[^2].Name : crumbs.Count == 2 ? crumbs[1].Name : string.Empty;
        string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        switch (page.Source)
        {
            case "Course":
            {
                // "{Category} · {Level} · {N} minutes · free"
                var facts = Split(paragraphs.FirstOrDefault());
                var lessons = page.Content.OfType<LinkListNode>().SelectMany(l => l.Items).Count(i => i.Href.StartsWith(page.Path + "/", StringComparison.Ordinal));
                var list = new List<string> { "Free course" };
                if (lessons > 0) list.Add(SocialCardText.Count(lessons, "lesson", "lessons"));
                list.AddRange(facts.Skip(1).Where(f => !f.Equals("free", StringComparison.OrdinalIgnoreCase)).Take(2));
                list.Add("Certificate");
                return new SocialCard(Join("Academy", page.Section ?? facts.FirstOrDefault()), title, page.Description, list);
            }
            case "Lesson":
            {
                // "{Course} · {Module} · lesson N of M · N min"
                var facts = Split(paragraphs.FirstOrDefault());
                var list = facts.Where(f => LessonFact().IsMatch(f)).Select(Capitalize).ToList();
                list.Add("Free");
                var course = crumbs.Count >= 3 ? crumbs[2].Name : null;
                return new SocialCard(Join("Lesson", course), title, page.Description, list);
            }
            case "Academy":
                return new SocialCard(Join(siteName, "Academy"), title, page.Description, new[] { "Free courses", "Verified certificates", "Self-paced" });
            case "Certificate":
                return new SocialCard("Verified certificate", title, page.Description);
            case "Blog post":
                return new SocialCard(Join("Blog", page.Section), title, page.Description, Split(metaLine));
            case "Service":
                return new SocialCard(Join("Service", paragraphs.FirstOrDefault()), title, page.Description);
            case "Case study":
                return new SocialCard(Join("Case study", Split(paragraphs.FirstOrDefault()).FirstOrDefault()), title, page.Description);
            case "Industry":
                return new SocialCard("Industry", title, page.Description);
            case "Job opening":
                return new SocialCard(Join("Careers", paragraphs.FirstOrDefault()), title, page.Description);
            case "Partner":
            case "Partners":
                return new SocialCard("Partners", title, page.Description);
            case "Public campaign":
                return new SocialCard("Creator campaign", title, page.Description);
        }
        if (page.Path == "/")
            return new SocialCard(paragraphs.FirstOrDefault() ?? siteName, title, page.Description);
        var eyebrow = Parent();
        if (string.IsNullOrWhiteSpace(eyebrow) || eyebrow.Equals(title, StringComparison.OrdinalIgnoreCase)) eyebrow = siteName;
        return new SocialCard(eyebrow, title, page.Description);
    }

    /// <summary>The paragraph right after the h1 (a meta line such as "Author · date · 6 min read").</summary>
    private static string? MetaLineAfterH1(SeoPage page)
    {
        var i = page.Content.FindIndex(n => n is HeadingNode { Level: 1 });
        return i >= 0 && i + 1 < page.Content.Count && page.Content[i + 1] is ParagraphNode p ? p.Text : null;
    }

    private static List<string> Split(string? line) =>
        string.IsNullOrWhiteSpace(line) ? new List<string>() : line.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string StripSite(string title, string siteName)
    {
        foreach (var sep in new[] { " | ", " — ", " · ", " - " })
        {
            var i = title.LastIndexOf(sep + siteName, StringComparison.Ordinal);
            if (i > 0) return title[..i];
        }
        return title;
    }

    [GeneratedRegex(@"^(lesson \d+ of \d+|\d+ min)$", RegexOptions.IgnoreCase)]
    private static partial Regex LessonFact();
}
