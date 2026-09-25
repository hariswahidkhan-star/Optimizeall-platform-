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

    /// <summary>
    /// Pack v2 (2026-09): the month the content was last reviewed for accuracy ("YYYY-MM"). A pack that declares it is a
    /// v2 pack (deeper lessons, a video lecture script on every lesson). Omitted from the stored JSON when null, so v1
    /// documents keep their exact serialization (and content hash).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastReviewed { get; set; }

    /// <summary>Pack v2: real tools/platforms the course teaches hands-on (0–20 names, ≤ 40 characters each).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tools { get; set; }

    /// <summary>A v2 pack: it declares <see cref="LastReviewed"/>.</summary>
    [JsonIgnore] public bool IsV2 => LastReviewed is not null;

    /// <summary>Total produced-or-planned lecture time (sum of every lesson's lecture target minutes).</summary>
    [JsonIgnore] public int LectureMinutes => AllLessons.Sum(x => x.Lesson.Lecture?.TargetMinutes ?? 0);

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

    /// <summary>Pack v2: the lesson's video lecture (production-ready script; media once produced). Required in v2 packs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PackLecture? Lecture { get; set; }

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

/// <summary>
/// A lesson's video lecture (pack v2): a scene-by-scene script produced later with ElevenLabs (voice + AI visuals, no
/// avatars). <see cref="Src"/>/<see cref="Poster"/>/<see cref="Captions"/> stay null until the lecture is produced; the
/// learner then sees the player, before that the chapters and the full transcript.
/// </summary>
public sealed class PackLecture
{
    /// <summary>Optional title when it differs from the lesson title (≤ 100 characters).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>4–14 minutes (≈ narration words / 140).</summary>
    public int TargetMinutes { get; set; }

    public List<PackScene>? Scenes { get; set; } = new();

    /// <summary>Terms the voice may mispronounce (acronyms, brands), with how to say them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PackPronunciation>? Pronunciations { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Src { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Poster { get; set; }

    /// <summary>WebVTT captions (needs <see cref="Src"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Captions { get; set; }

    /// <summary>Total narration words.</summary>
    [JsonIgnore] public int NarrationWords => (Scenes ?? new()).Sum(s => CoursePackValidator.WordCount(s?.Narration));

    /// <summary>Total planned scene time in seconds.</summary>
    [JsonIgnore] public int TotalSeconds => (Scenes ?? new()).Sum(s => s?.Seconds ?? 0);

    /// <summary>The spoken transcript: every scene's narration, one paragraph per scene.</summary>
    [JsonIgnore] public string Transcript => string.Join("\n\n", (Scenes ?? new()).Where(s => s is not null).Select(s => s.Narration.Trim()));
}

/// <summary>One scene of a lecture: what the voice says, the slide text, the visual direction and its planned length.</summary>
public sealed class PackScene
{
    /// <summary>Spoken English, 40–260 words, no Markdown or URLs.</summary>
    public string Narration { get; set; } = string.Empty;

    /// <summary>Slide text: a title line, then 0–4 "• " bullet lines separated by newlines (≤ 320 characters).</summary>
    public string OnScreen { get; set; } = string.Empty;

    /// <summary>Direction for the video generator/editor (≤ 400 characters).</summary>
    public string Visual { get; set; } = string.Empty;

    /// <summary>15–150 seconds (≈ narration words / 2.3).</summary>
    public int Seconds { get; set; }

    /// <summary>The chapter title: the first line of <see cref="OnScreen"/> (bullet marker removed).</summary>
    [JsonIgnore]
    public string ChapterTitle
    {
        get
        {
            var first = (OnScreen ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
            return first.TrimStart('•', '-', '*', ' ').Trim();
        }
    }
}

public sealed class PackPronunciation
{
    public string Term { get; set; } = string.Empty;
    public string Say { get; set; } = string.Empty;
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
