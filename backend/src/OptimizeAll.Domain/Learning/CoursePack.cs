using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimizeAll.Domain.Learning;

/// <summary>
/// A course document in the course-pack format (docs/LEARNING.md § "Course pack contract"): the JSON files in
/// <c>Api/Modules/Learning/Catalog/*.json</c> and every admin-authored course version use this exact shape. Enumerated
/// values stay strings here so <see cref="CoursePackValidator"/> can report them precisely; the typed accessors map them
/// once the pack is valid.
/// </summary>
public sealed class CoursePack
{
    public string Slug { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string>? Outcomes { get; set; } = new();
    public List<string>? Skills { get; set; } = new();
    public List<string>? Prerequisites { get; set; } = new();
    public PackBadge? Badge { get; set; }
    public int PassingScore { get; set; }
    public List<PackModule>? Modules { get; set; } = new();
    public PackExam? FinalExam { get; set; }

    [JsonIgnore] public CourseCategory CategoryValue => LearningEnums.ParseCategory(Category) ?? CourseCategory.Platform;
    [JsonIgnore] public CourseLevel LevelValue => LearningEnums.ParseLevel(Level) ?? CourseLevel.Beginner;

    /// <summary>Every lesson in syllabus order.</summary>
    [JsonIgnore]
    public IEnumerable<(PackModule Module, PackLesson Lesson)> AllLessons =>
        (Modules ?? new()).SelectMany(m => (m.Lessons ?? new()).Select(l => (m, l)));

    [JsonIgnore] public int LessonCount => AllLessons.Count();

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        // Schema strictness: an unknown (e.g. misspelled) property is an error, not silently ignored.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Parses a pack; a JSON or shape error is returned as the error text (never thrown).</summary>
    public static (CoursePack? Pack, string? Error) Parse(string json)
    {
        try
        {
            var pack = JsonSerializer.Deserialize<CoursePack>(json, ReadOptions);
            return pack is null ? (null, "The document is empty.") : (pack, null);
        }
        catch (JsonException ex)
        {
            // Path + line only: the serializer message names CLR types.
            return (null, $"Invalid JSON at {ex.Path ?? "$"} (line {(ex.LineNumber ?? 0) + 1}): the value does not match the course pack format.");
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, WriteOptions);

    /// <summary>A deep copy (JSON round trip).</summary>
    public CoursePack Clone() => Parse(ToJson()).Pack!;
}

public sealed class PackBadge
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Criteria { get; set; } = string.Empty;
}

public sealed class PackModule
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<PackLesson>? Lessons { get; set; } = new();
}

public sealed class PackLesson
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = "article";
    public int DurationMinutes { get; set; }
    public string Body { get; set; } = string.Empty;
    public PackVideo? Video { get; set; }
    public List<string>? KeyTakeaways { get; set; } = new();
    public List<PackCheck>? KnowledgeCheck { get; set; } = new();
    public string? Activity { get; set; }

    [JsonIgnore] public LessonType TypeValue => string.Equals(Type, "video", StringComparison.Ordinal) ? LessonType.Video : LessonType.Article;
}

/// <summary>Video block of a video lesson. <see cref="Src"/> stays null until the video is produced (ElevenLabs/HeyGen).</summary>
public sealed class PackVideo
{
    public string Script { get; set; } = string.Empty;
    public string? Src { get; set; }
    public string? Poster { get; set; }
    public string? Captions { get; set; }
}

/// <summary>A non-graded knowledge-check question shown after a lesson.</summary>
public sealed class PackCheck
{
    public string Question { get; set; } = string.Empty;
    public List<string>? Options { get; set; } = new();
    public List<int>? Correct { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public sealed class PackExam
{
    public int QuestionCount { get; set; }
    public int TimeLimitMinutes { get; set; }
    public int MaxAttemptsPerDay { get; set; }
    public List<PackQuestion>? Pool { get; set; } = new();
}

/// <summary>A final-exam question. Its <see cref="Correct"/> indices never leave the server before the attempt is submitted.</summary>
public sealed class PackQuestion
{
    public string Id { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string>? Options { get; set; } = new();
    public List<int>? Correct { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;

    [JsonIgnore] public QuestionType TypeValue => string.Equals(Type, "multiple", StringComparison.Ordinal) ? QuestionType.Multiple : QuestionType.Single;
    [JsonIgnore] public QuestionDifficulty DifficultyValue => LearningEnums.ParseDifficulty(Difficulty) ?? QuestionDifficulty.Medium;
}
