using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Learning;

/// <summary>How strictly <see cref="CoursePackValidator"/> checks a course document.</summary>
public enum PackValidationMode
{
    /// <summary>Course packs (CI and startup): every rule of the contract, including the word-count ranges.</summary>
    Strict,
    /// <summary>
    /// Admin-authored versions: every structural, count and question rule of the contract (identical MCQ validation),
    /// without the editorial word-count ranges (description, badge description, lesson body, video script).
    /// </summary>
    Authoring,
}

public sealed record PackIssue(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// The course pack contract as code (docs/LEARNING.md § "Course pack contract"). Used by the startup catalog upsert, by the
/// admin course editor (Authoring mode) and by the <c>CoursePackTests</c> unit test that checks every pack in CI.
/// </summary>
public static partial class CoursePackValidator
{
    public const int MaxTitle = 80;
    public const int MaxSubtitle = 140;
    public const int MaxOutcome = 140;
    public const int MaxBadgeName = 60;
    public const double PoolFactor = 1.5;

    // Pack v2 (docs/LEARNING.md § "Pack v2"). A pack that declares lastReviewed is v2.
    public const int MaxTools = 20;
    public const int MaxTool = 40;
    public const int V1BodyMin = 500, V1BodyMax = 1100;
    public const int V2BodyMin = 700, V2BodyMax = 1800;
    public const int LectureMinutesMin = 4, LectureMinutesMax = 14;
    public const int ScenesMin = 5, ScenesMax = 16;
    public const int SceneWordsMin = 40, SceneWordsMax = 260;
    public const int LectureWordsMin = 600, LectureWordsMax = 1800;
    public const int SceneSecondsMin = 15, SceneSecondsMax = 150;
    public const int MaxOnScreen = 320, MaxVisual = 400, MaxLectureTitle = 100;
    public const int MaxPronunciations = 30;
    /// <summary>Speaking rate used to check targetMinutes: ≈ 140 words per minute, within ± 3 minutes.</summary>
    public const double WordsPerMinute = 140, TargetMinutesTolerance = 3;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex QuestionIdRegex();

    [GeneratedRegex(@"^\s{0,3}#{1,2}(\s|$)", RegexOptions.Multiline)]
    private static partial Regex TopHeadingRegex();

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z!][^>]*>")]
    private static partial Regex HtmlRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?(?:[a-zA-Z][a-zA-Z0-9+.-]*:|//)")]
    private static partial Regex ExternalImageRegex();

    [GeneratedRegex(@"^20\d\d-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthRegex();

    /// <summary>Narration is spoken: no Markdown symbols and no URLs read aloud.</summary>
    [GeneratedRegex(@"[#*`\[\]]|https?://")]
    private static partial Regex NarrationMarkupRegex();

    [GeneratedRegex(@"^/api/v1/files/[0-9a-fA-F-]{36}$")]
    private static partial Regex UploadUrlRegex();

    private static readonly string[] BannedOptions = { "all of the above", "none of the above" };

    public static bool IsSlug(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 80 && SlugRegex().IsMatch(value);

    public static bool IsQuestionId(string? value) => !string.IsNullOrEmpty(value) && QuestionIdRegex().IsMatch(value);

    /// <summary>An uploaded file (<c>/api/v1/files/{id}</c>) or an https URL (e.g. a HeyGen-hosted video).</summary>
    public static bool IsMediaUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 500 &&
        (UploadUrlRegex().IsMatch(value) ||
         (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)));

    [GeneratedRegex(@"(?ms)^\s*(```|~~~).*?^\s*\1[`~]*\s*$")]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"`[^`\n]*`")]
    private static partial Regex InlineCodeRegex();

    /// <summary>Markdown without fenced code blocks and inline code (where HTML examples are legitimate text).</summary>
    public static string StripCode(string markdown) => InlineCodeRegex().Replace(FencedCodeRegex().Replace(markdown, string.Empty), string.Empty);

    public static int WordCount(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The minimum pool size for <paramref name="questionCount"/> drawn questions (1.5 ×, rounded up).</summary>
    public static int MinimumPool(int questionCount) => (int)Math.Ceiling(questionCount * PoolFactor);

    public static IReadOnlyList<PackIssue> Validate(CoursePack pack, PackValidationMode mode = PackValidationMode.Strict)
    {
        var v = new Collector(mode == PackValidationMode.Strict);

        if (!IsSlug(pack.Slug)) v.Add("slug", "Use kebab-case: lower-case letters, digits and single hyphens (max 80 characters).");
        if (pack.Version is < 1 or > 100_000) v.Add("version", "Use a whole number of at least 1.");
        v.Text("title", pack.Title, MaxTitle);
        v.Text("subtitle", pack.Subtitle, MaxSubtitle);
        if (LearningEnums.ParseCategory(pack.Category) is null)
            v.Add("category", $"Use one of: {string.Join(", ", LearningEnums.Categories)}.");
        if (LearningEnums.ParseLevel(pack.Level) is null)
            v.Add("level", $"Use one of: {string.Join(", ", LearningEnums.Levels)}.");
        if (pack.EstimatedMinutes is < 1 or > 6000) v.Add("estimatedMinutes", "Use a whole number of minutes from 1 to 6000.");
        v.Markdown("description", pack.Description, 4000);
        v.Words("description", pack.Description, 80, 200);

        v.List("outcomes", pack.Outcomes, 5, 8, MaxOutcome);
        v.List("skills", pack.Skills, 3, 8, 40);
        v.Unique("skills", pack.Skills);

        var v2 = pack.IsV2;
        if (pack.LastReviewed is not null && !MonthRegex().IsMatch(pack.LastReviewed))
            v.Add("lastReviewed", "Use the review month as YYYY-MM (e.g. 2026-09).");
        if (pack.Tools is not null)
        {
            if (pack.Tools.Count > MaxTools) v.Add("tools", $"List at most {MaxTools} tools.");
            for (var i = 0; i < pack.Tools.Count; i++) v.Text($"tools[{i}]", pack.Tools[i], MaxTool);
            v.Unique("tools", pack.Tools);
        }

        var prerequisites = pack.Prerequisites ?? new List<string>();
        if (pack.Prerequisites is null) v.Add("prerequisites", "Use an empty list when there are none.");
        for (var i = 0; i < prerequisites.Count; i++)
        {
            if (!IsSlug(prerequisites[i])) v.Add($"prerequisites[{i}]", "Use the prerequisite course's slug.");
            else if (prerequisites[i] == pack.Slug) v.Add($"prerequisites[{i}]", "A course cannot be its own prerequisite.");
        }
        v.Unique("prerequisites", prerequisites);

        if (pack.Badge is null) v.Add("badge", "The badge (name, description, criteria) is required.");
        else
        {
            v.Text("badge.name", pack.Badge.Name, MaxBadgeName);
            v.Text("badge.description", pack.Badge.Description, 1000);
            v.Words("badge.description", pack.Badge.Description, 30, 80);
            v.Text("badge.criteria", pack.Badge.Criteria, 500);
        }
        if (pack.PassingScore is < 50 or > 100) v.Add("passingScore", "Use a percentage from 50 to 100.");

        var modules = pack.Modules ?? new List<PackModule>();
        if (modules.Count is < 1 or > 30) v.Add("modules", "A course has 1 to 30 modules.");
        var moduleSlugs = new HashSet<string>(StringComparer.Ordinal);
        var lessonSlugs = new HashSet<string>(StringComparer.Ordinal);
        for (var m = 0; m < modules.Count; m++)
        {
            var module = modules[m];
            var mp = $"modules[{m}]";
            if (module is null) { v.Add(mp, "A module cannot be null."); continue; }
            if (!IsSlug(module.Slug)) v.Add($"{mp}.slug", "Use a kebab-case slug.");
            else if (!moduleSlugs.Add(module.Slug)) v.Add($"{mp}.slug", $"Module slug '{module.Slug}' is used twice.");
            v.Text($"{mp}.title", module.Title, 120);
            v.Text($"{mp}.summary", module.Summary, 400);
            var lessons = module.Lessons ?? new List<PackLesson>();
            if (lessons.Count is < 1 or > 40) v.Add($"{mp}.lessons", "A module has 1 to 40 lessons.");
            for (var l = 0; l < lessons.Count; l++)
            {
                var lp = $"{mp}.lessons[{l}]";
                if (lessons[l] is null) { v.Add(lp, "A lesson cannot be null."); continue; }
                ValidateLesson(v, lp, lessons[l], lessonSlugs, v2);
            }
        }

        if (pack.FinalExam is null) v.Add("finalExam", "The final exam is required.");
        else ValidateExam(v, pack.FinalExam, moduleSlugs);

        return v.Issues;
    }

    private static void ValidateLesson(Collector v, string lp, PackLesson lesson, HashSet<string> lessonSlugs, bool v2)
    {
        if (!IsSlug(lesson.Slug)) v.Add($"{lp}.slug", "Use a kebab-case slug.");
        else if (!lessonSlugs.Add(lesson.Slug)) v.Add($"{lp}.slug", $"Lesson slug '{lesson.Slug}' is used twice in this course.");
        v.Text($"{lp}.title", lesson.Title, 120);
        if (lesson.Type is not ("article" or "video")) v.Add($"{lp}.type", "Use article or video.");
        if (lesson.DurationMinutes is < 1 or > 240) v.Add($"{lp}.durationMinutes", "Use a whole number of minutes from 1 to 240.");
        v.Markdown($"{lp}.body", lesson.Body, 20_000);
        if (v2) v.Words($"{lp}.body", lesson.Body, V2BodyMin, V2BodyMax);
        else v.Words($"{lp}.body", lesson.Body, V1BodyMin, V1BodyMax);
        // Only real headings count: "#" lines inside fenced code (robots.txt/shell comments, Markdown samples) are code.
        if (!string.IsNullOrEmpty(lesson.Body) && TopHeadingRegex().IsMatch(StripCode(lesson.Body)))
            v.Add($"{lp}.body", "Headings start at ### (the page already has the lesson title).");

        if (lesson.Type == "video")
        {
            if (lesson.Video is null) v.Add($"{lp}.video", "A video lesson needs a video block with its narration script.");
            else
            {
                v.Text($"{lp}.video.script", lesson.Video.Script, 5000);
                v.Words($"{lp}.video.script", lesson.Video.Script, 150, 400);
                v.Media($"{lp}.video.src", lesson.Video.Src);
                v.Media($"{lp}.video.poster", lesson.Video.Poster);
                v.Media($"{lp}.video.captions", lesson.Video.Captions);
                if (lesson.Video.Captions is not null && lesson.Video.Src is null)
                    v.Add($"{lp}.video.captions", "Captions need a video source.");
            }
        }
        else if (lesson.Type == "article" && lesson.Video is not null)
        {
            v.Add($"{lp}.video", "Only video lessons have a video block (use null).");
        }

        if (lesson.Lecture is not null) ValidateLecture(v, $"{lp}.lecture", lesson.Lecture);
        else if (v2) v.Add($"{lp}.lecture", "A v2 pack (it declares lastReviewed) needs a video lecture on every lesson.");

        v.List($"{lp}.keyTakeaways", lesson.KeyTakeaways, 3, 5, 300);

        var checks = lesson.KnowledgeCheck ?? new List<PackCheck>();
        if (lesson.KnowledgeCheck is null || checks.Count is < 2 or > 4) v.Add($"{lp}.knowledgeCheck", "Add 2 to 4 knowledge-check questions.");
        for (var q = 0; q < checks.Count; q++)
        {
            var qp = $"{lp}.knowledgeCheck[{q}]";
            var check = checks[q];
            if (check is null) { v.Add(qp, "A question cannot be null."); continue; }
            v.Text($"{qp}.question", check.Question, 500);
            ValidateOptions(v, qp, check.Options, 2, 5, banned: false);
            ValidateCorrect(v, qp, check.Correct, check.Options?.Count ?? 0, null);
            v.Text($"{qp}.explanation", check.Explanation, 1500);
        }

        if (lesson.Activity is not null && (lesson.Activity.Trim().Length == 0 || lesson.Activity.Length > 600))
            v.Add($"{lp}.activity", "Write the activity in 1 to 3 sentences (max 600 characters), or use null.");
        if (lesson.Activity is not null && HtmlRegex().IsMatch(StripCode(lesson.Activity))) v.Add($"{lp}.activity", "No HTML.");
    }

    private static void ValidateLecture(Collector v, string lp, PackLecture lecture)
    {
        if (lecture.Title is not null) v.Text($"{lp}.title", lecture.Title, MaxLectureTitle);
        if (lecture.TargetMinutes is < LectureMinutesMin or > LectureMinutesMax)
            v.Add($"{lp}.targetMinutes", $"Use {LectureMinutesMin} to {LectureMinutesMax} minutes (≈ narration words / 140).");
        var scenes = lecture.Scenes ?? new List<PackScene>();
        if (lecture.Scenes is null || scenes.Count is < ScenesMin or > ScenesMax)
            v.Add($"{lp}.scenes", $"A lecture has {ScenesMin} to {ScenesMax} scenes (it has {scenes.Count}).");
        var words = 0;
        for (var i = 0; i < scenes.Count; i++)
        {
            var sp = $"{lp}.scenes[{i}]";
            var scene = scenes[i];
            if (scene is null) { v.Add(sp, "A scene cannot be null."); continue; }
            v.Text($"{sp}.narration", scene.Narration, 2500);
            words += WordCount(scene.Narration);
            v.Words($"{sp}.narration", scene.Narration, SceneWordsMin, SceneWordsMax);
            if (!string.IsNullOrEmpty(scene.Narration) && NarrationMarkupRegex().IsMatch(scene.Narration))
                v.Add($"{sp}.narration", "Narration is spoken: no Markdown symbols (# * ` [ ]) and no URLs.");
            v.Text($"{sp}.onScreen", scene.OnScreen, MaxOnScreen);
            v.Text($"{sp}.visual", scene.Visual, MaxVisual);
            if (scene.Seconds is < SceneSecondsMin or > SceneSecondsMax)
                v.Add($"{sp}.seconds", $"Use {SceneSecondsMin} to {SceneSecondsMax} seconds (≈ narration words / 2.3).");
        }
        if (v.Strict && scenes.Count > 0)
        {
            if (words is < LectureWordsMin or > LectureWordsMax)
                v.Add($"{lp}.scenes", $"The narration totals {LectureWordsMin}–{LectureWordsMax} words (it has {words}).");
            if (words > 0 && lecture.TargetMinutes is >= LectureMinutesMin and <= LectureMinutesMax &&
                Math.Abs(words / WordsPerMinute - lecture.TargetMinutes) > TargetMinutesTolerance)
                v.Add($"{lp}.targetMinutes", $"{lecture.TargetMinutes} minutes is far from {words} words / 140 (≈ {Math.Round(words / WordsPerMinute)}).");
        }
        var pronunciations = lecture.Pronunciations ?? new List<PackPronunciation>();
        if (pronunciations.Count > MaxPronunciations) v.Add($"{lp}.pronunciations", $"List at most {MaxPronunciations} pronunciations.");
        for (var i = 0; i < pronunciations.Count; i++)
        {
            var pp = $"{lp}.pronunciations[{i}]";
            if (pronunciations[i] is null) { v.Add(pp, "A pronunciation cannot be null."); continue; }
            v.Text($"{pp}.term", pronunciations[i].Term, 60);
            v.Text($"{pp}.say", pronunciations[i].Say, 120);
        }
        v.Media($"{lp}.src", lecture.Src);
        v.Media($"{lp}.poster", lecture.Poster);
        v.Media($"{lp}.captions", lecture.Captions);
        if (lecture.Captions is not null && lecture.Src is null) v.Add($"{lp}.captions", "Captions need a lecture video source.");
    }

    private static void ValidateExam(Collector v, PackExam exam, HashSet<string> moduleSlugs)
    {
        if (exam.QuestionCount is < 1 or > 100) v.Add("finalExam.questionCount", "Draw 1 to 100 questions per attempt.");
        if (exam.TimeLimitMinutes is < 1 or > 240) v.Add("finalExam.timeLimitMinutes", "Use a time limit from 1 to 240 minutes.");
        if (exam.MaxAttemptsPerDay is < 1 or > 20) v.Add("finalExam.maxAttemptsPerDay", "Allow 1 to 20 attempts per day.");
        var pool = exam.Pool ?? new List<PackQuestion>();
        if (pool.Count > 500) v.Add("finalExam.pool", "The pool holds at most 500 questions.");
        var minimum = MinimumPool(Math.Max(1, exam.QuestionCount));
        if (pool.Count < minimum)
            v.Add("finalExam.pool", $"The pool needs at least {minimum} questions (1.5 × questionCount {exam.QuestionCount}); it has {pool.Count}.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < pool.Count; i++)
        {
            var qp = $"finalExam.pool[{i}]";
            var q = pool[i];
            if (q is null) { v.Add(qp, "A question cannot be null."); continue; }
            if (!IsQuestionId(q.Id)) v.Add($"{qp}.id", "Use a stable id of lower-case letters, digits and hyphens (max 64).");
            else if (!ids.Add(q.Id)) v.Add($"{qp}.id", $"Question id '{q.Id}' is used twice.");
            if (!moduleSlugs.Contains(q.Module)) v.Add($"{qp}.module", $"'{q.Module}' is not a module slug of this course.");
            else covered.Add(q.Module);
            if (LearningEnums.ParseDifficulty(q.Difficulty) is null) v.Add($"{qp}.difficulty", "Use easy, medium or hard.");
            if (q.Type is not ("single" or "multiple")) v.Add($"{qp}.type", "Use single or multiple.");
            v.Text($"{qp}.question", q.Question, 1000);
            ValidateOptions(v, qp, q.Options, 3, 5, banned: true);
            ValidateCorrect(v, qp, q.Correct, q.Options?.Count ?? 0, q.Type);
            v.Text($"{qp}.explanation", q.Explanation, 2000);
        }
        foreach (var module in moduleSlugs.Where(m => !covered.Contains(m)))
            v.Add("finalExam.pool", $"Module '{module}' has no exam questions: the pool must cover every module.");
    }

    private static void ValidateOptions(Collector v, string qp, List<string>? options, int min, int max, bool banned)
    {
        if (options is null || options.Count < min || options.Count > max)
        {
            v.Add($"{qp}.options", $"Give {min} to {max} options.");
            return;
        }
        // Exact duplicates only: options that differ in capitalisation are distinct answers (e.g. #smallbusinesstips vs
        // #SmallBusinessTips when the question is about capitalisation).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var o = 0; o < options.Count; o++)
        {
            var text = options[o]?.Trim() ?? string.Empty;
            if (text.Length is 0 or > 300) v.Add($"{qp}.options[{o}]", "Write the option (1 to 300 characters).");
            else if (!seen.Add(text)) v.Add($"{qp}.options[{o}]", "Two options have the same text.");
            if (banned && BannedOptions.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)))
                v.Add($"{qp}.options[{o}]", "Do not use 'all of the above' or 'none of the above'.");
        }
    }

    private static void ValidateCorrect(Collector v, string qp, List<int>? correct, int optionCount, string? type)
    {
        if (correct is null || correct.Count == 0)
        {
            v.Add($"{qp}.correct", "Mark at least one correct option.");
            return;
        }
        if (correct.Any(c => c < 0 || c >= optionCount))
            v.Add($"{qp}.correct", $"Correct indices must be between 0 and {Math.Max(0, optionCount - 1)}.");
        if (correct.Distinct().Count() != correct.Count) v.Add($"{qp}.correct", "A correct index is listed twice.");
        if (type == "single" && correct.Count != 1) v.Add($"{qp}.correct", "A single-choice question has exactly one correct option.");
        if (type == "multiple" && (correct.Count < 2 || correct.Count >= optionCount))
            v.Add($"{qp}.correct", "A multiple-choice question has at least two correct options and at least one incorrect option.");
    }

    private sealed class Collector(bool strict)
    {
        public List<PackIssue> Issues { get; } = new();

        /// <summary>Editorial ranges (word counts, pacing) apply: course packs, not admin authoring.</summary>
        public bool Strict => strict;

        public void Add(string path, string message) => Issues.Add(new PackIssue(path, message));

        public void Text(string path, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) Add(path, "Required.");
            else if (value.Length > max) Add(path, $"Keep it to {max} characters (it has {value.Length}).");
        }

        public void Markdown(string path, string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) { Add(path, "Required."); return; }
            if (value.Length > max) Add(path, $"Keep it to {max} characters (it has {value.Length}).");
            var prose = StripCode(value);
            if (HtmlRegex().IsMatch(prose)) Add(path, "No HTML: use Markdown (HTML examples belong in `code` or fenced code blocks).");
            if (ExternalImageRegex().IsMatch(prose)) Add(path, "No external images.");
        }

        public void Words(string path, string? value, int min, int max)
        {
            if (!strict || string.IsNullOrWhiteSpace(value)) return;
            var words = WordCount(value);
            if (words < min || words > max) Add(path, $"Write {min}–{max} words (it has {words}).");
        }

        public void List(string path, List<string>? items, int min, int max, int maxLength)
        {
            if (items is null || items.Count < min || items.Count > max)
            {
                Add(path, $"Give {min} to {max} items.");
                if (items is null) return;
            }
            for (var i = 0; i < items.Count; i++) Text($"{path}[{i}]", items[i], maxLength);
        }

        public void Unique(string path, List<string>? items)
        {
            if (items is null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.Where(i => i is not null))
                if (!seen.Add(item.Trim())) Add(path, $"'{item}' is listed twice.");
        }

        public void Media(string path, string? value)
        {
            if (value is not null && !IsMediaUrl(value)) Add(path, "Use an uploaded file (/api/v1/files/{id}) or an https URL, or null.");
        }
    }
}
