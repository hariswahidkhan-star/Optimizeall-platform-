using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizeAll.Domain.Learning;

/// <summary>
/// A learning path (docs/LEARNING.md § "Learning paths"): an ordered sequence of courses towards a role or goal, defined in
/// <c>Api/Modules/Learning/Catalog/paths/*.json</c>. Courses are referenced by slug; a slug that is not (yet) published is
/// skipped at runtime, so a path can name planned courses before their content lands.
/// </summary>
public sealed class LearningPathDefinition
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    /// <summary>Markdown, 60–220 words.</summary>
    public string Description { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    /// <summary>Course slugs in the recommended order (3–14).</summary>
    public List<string>? Courses { get; set; } = new();
    /// <summary>4–8 outcomes, each ≤ 140 characters.</summary>
    public List<string>? Outcomes { get; set; } = new();
    /// <summary>Who the path is for: 2–6 short statements.</summary>
    public List<string>? Audience { get; set; } = new();
    /// <summary>Order on the paths page (lower first).</summary>
    public int SortOrder { get; set; }

    [JsonIgnore] public CourseLevel LevelValue => LearningEnums.ParseLevel(Level) ?? CourseLevel.Beginner;

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Parses a path file; a JSON or shape error is returned as text (never thrown).</summary>
    public static (LearningPathDefinition? Path, string? Error) Parse(string json)
    {
        try
        {
            var path = JsonSerializer.Deserialize<LearningPathDefinition>(json, ReadOptions);
            return path is null ? (null, "The document is empty.") : (path, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Invalid JSON at {ex.Path ?? "$"} (line {(ex.LineNumber ?? 0) + 1}): the value does not match the learning path format.");
        }
    }
}

/// <summary>The learning path contract as code (used by the path library and the <c>LearningPathTests</c> unit test).</summary>
public static class LearningPathValidator
{
    public const int MinCourses = 3, MaxCourses = 14;

    public static IReadOnlyList<PackIssue> Validate(LearningPathDefinition path)
    {
        var issues = new List<PackIssue>();
        void Add(string p, string m) => issues.Add(new PackIssue(p, m));
        void Text(string p, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) Add(p, "Required.");
            else if (value.Length > max) Add(p, $"Keep it to {max} characters (it has {value.Length}).");
        }
        void List(string p, List<string>? items, int min, int max, int maxLength)
        {
            if (items is null || items.Count < min || items.Count > max) Add(p, $"Give {min} to {max} items.");
            for (var i = 0; i < (items?.Count ?? 0); i++) Text($"{p}[{i}]", items![i], maxLength);
        }

        if (!CoursePackValidator.IsSlug(path.Slug)) Add("slug", "Use kebab-case: lower-case letters, digits and single hyphens.");
        Text("title", path.Title, 80);
        Text("subtitle", path.Subtitle, 140);
        Text("description", path.Description, 3000);
        var words = CoursePackValidator.WordCount(path.Description);
        if (!string.IsNullOrWhiteSpace(path.Description) && words is < 60 or > 220) Add("description", $"Write 60–220 words (it has {words}).");
        if (LearningEnums.ParseLevel(path.Level) is null) Add("level", $"Use one of: {string.Join(", ", LearningEnums.Levels)}.");
        var courses = path.Courses ?? new List<string>();
        if (path.Courses is null || courses.Count is < MinCourses or > MaxCourses) Add("courses", $"A path has {MinCourses} to {MaxCourses} courses.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < courses.Count; i++)
        {
            if (!CoursePackValidator.IsSlug(courses[i])) Add($"courses[{i}]", "Use the course's slug.");
            else if (!seen.Add(courses[i])) Add($"courses[{i}]", $"'{courses[i]}' is listed twice.");
        }
        List("outcomes", path.Outcomes, 4, 8, 140);
        List("audience", path.Audience, 2, 6, 140);
        if (path.SortOrder is < 0 or > 10_000) Add("sortOrder", "Use 0 to 10000.");
        return issues;
    }
}
