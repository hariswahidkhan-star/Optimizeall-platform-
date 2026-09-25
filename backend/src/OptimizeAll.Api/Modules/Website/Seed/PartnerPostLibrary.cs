using System.Reflection;

namespace OptimizeAll.Api.Modules.Website.Seed;

/// <summary>A partner-content blog post read from <c>Modules/Website/Content/partner-posts/*.md</c> (front matter + Markdown body).</summary>
public sealed record PartnerPost(
    string Slug,
    string Title,
    string Description,
    string Cluster,
    string PrimaryKeyword,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Related,
    int PublishedDaysAgo,
    string Cover,
    string CoverAlt,
    string Body);

/// <summary>
/// Loads the partner-content posts (PCI AI and Certuvo clusters) embedded in the API assembly. Each file starts with a
/// front-matter block between <c>---</c> lines holding <c>key: value</c> pairs (lists are comma-separated):
/// slug, title, description, cluster, primaryKeyword, categories, tags, related, publishedDaysAgo, cover, coverAlt.
/// </summary>
public static class PartnerPostLibrary
{
    public const string PostPrefix = "OptimizeAll.PartnerPosts.";
    public const string CoverPrefix = "OptimizeAll.PartnerPosts.covers.";

    /// <summary>The disclosure line every partner post ends with.</summary>
    public const string Disclosure = "Optimize All is the official marketing partner of PCI AI and Certuvo.";

    /// <summary>The partner sites the posts may link to (at most three such links per post).</summary>
    public static readonly string[] PartnerHosts = { "pciai.org", "certuvo.com" };

    /// <summary>Optimize All Academy course slugs the posts may link to (<c>/learn/{slug}</c>).</summary>
    public static readonly string[] CourseSlugs =
    {
        "project-controls-with-ai", "project-finance-and-financial-modelling", "project-management-leadership-with-ai",
        "professional-certification-exam-success", "leadership-and-communication", "ai-for-data-analysis-and-decision-making",
        "prompt-engineering-foundations", "advanced-prompt-engineering", "mastering-claude", "mastering-chatgpt",
    };

    /// <summary>Partner pages on this site the posts may link to.</summary>
    public static readonly string[] PartnerPages = { "/partners/pci-ai", "/partners/certuvo" };

    /// <summary>Blog categories the partner posts use (slug, name, description); created by the seeder when missing.</summary>
    public static readonly (string Slug, string Name, string Description)[] Categories =
    {
        ("project-controls", "Project controls & finance", "Planning, earned value, forecasting, project finance and governed AI for project professionals."),
        ("exam-prep", "Certification & exam prep", "Study plans and learning science for professional certification exams."),
    };

    private static readonly Lazy<IReadOnlyList<PartnerPost>> Loaded = new(Load);

    public static IReadOnlyList<PartnerPost> All => Loaded.Value;

    /// <summary>The PNG bytes of a cover (<c>covers/{name}.png</c>).</summary>
    public static byte[] CoverBytes(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CoverPrefix + name + ".png")
            ?? throw new InvalidOperationException($"Partner post cover '{name}' is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static IReadOnlyList<PartnerPost> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(PostPrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal) &&
                        !n.StartsWith(CoverPrefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return Parse(n, reader.ReadToEnd());
            })
            .OrderByDescending(p => p.PublishedDaysAgo)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Parses one post file. Throws with the resource name when the front matter is incomplete.</summary>
    public static PartnerPost Parse(string name, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
            throw new InvalidOperationException($"{name}: missing front matter.");
        var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
        if (end < 0) throw new InvalidOperationException($"{name}: unterminated front matter.");

        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines[1..end])
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new InvalidOperationException($"{name}: invalid front-matter line '{line}'.");
            meta[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        string Required(string key) =>
            meta.TryGetValue(key, out var v) && v.Length > 0 ? v : throw new InvalidOperationException($"{name}: front matter needs '{key}'.");
        IReadOnlyList<string> List(string key) => meta.TryGetValue(key, out var v)
            ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : Array.Empty<string>();

        return new PartnerPost(
            Required("slug"),
            Required("title"),
            Required("description"),
            Required("cluster"),
            Required("primaryKeyword"),
            List("categories"),
            List("tags"),
            List("related"),
            int.Parse(Required("publishedDaysAgo"), System.Globalization.CultureInfo.InvariantCulture),
            Required("cover"),
            Required("coverAlt"),
            string.Join('\n', lines[(end + 1)..]).Trim());
    }
}
