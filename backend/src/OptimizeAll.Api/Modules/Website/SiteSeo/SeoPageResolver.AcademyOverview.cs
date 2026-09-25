using System.Globalization;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// The academy sections the marketing pages share (home page and the /academy overview), server-rendered with the same
/// live figures, subjects, featured courses, suggested paths, "how it works" steps and certificate strip as the web app
/// (frontend/src/features/public/site/Showcase.tsx and academy.ts), and the same editable page copy — so the HTML
/// crawlers read matches what visitors see.
/// </summary>
public sealed partial class SeoPageResolver
{
    /// <summary>The live catalog as the web app's useAcademyOverview() reads it (one page of up to 200 courses, plus subjects).</summary>
    public sealed record AcademyOverview(int CourseCount, int LessonCount, bool LessonCountComplete, IReadOnlyList<CategorySummaryDto> Categories,
        IReadOnlyList<CourseCardDto> Courses, IReadOnlyList<CourseCardDto> Featured);

    private AcademyOverview? _academy;

    private async Task<AcademyOverview> AcademyOverviewAsync(CancellationToken ct)
    {
        if (_academy is not null) return _academy;
        var catalog = await learning.CatalogAsync(new CatalogQuery { Page = 1, PageSize = 200 }, ct);
        var categories = (await learning.CategoriesAsync(ct)).Where(c => c.CourseCount > 0).ToList();
        var courses = catalog.Items;
        // Subject courses lead; the platform's own onboarding course goes last (as the web app orders them).
        var featured = courses.Where(c => c.IsFeatured).OrderBy(c => c.Category == CourseCategory.Platform ? 1 : 0).ToList();
        return _academy = new AcademyOverview(catalog.Total, courses.Sum(c => c.LessonCount), courses.Count >= catalog.Total, categories, courses, featured);
    }

    private static string Minutes(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>Live figures: courses, lessons, subjects and the (zero) cost, labelled with the page copy.</summary>
    private FactsNode AcademyStatsNode(AcademyOverview a) => new(new[]
    {
        KeyValuePair.Create(_copy.Text("home.academy.statCourses"), a.CourseCount.ToString(CultureInfo.InvariantCulture)),
        KeyValuePair.Create(_copy.Text("home.academy.statLessons"), a.LessonCount.ToString(CultureInfo.InvariantCulture) + (a.LessonCountComplete ? string.Empty : "+")),
        KeyValuePair.Create(_copy.Text("home.academy.statSubjects"), a.Categories.Count.ToString(CultureInfo.InvariantCulture)),
        KeyValuePair.Create(_copy.Text("home.academy.statFree"), "$0"),
    });

    /// <summary>Subject links to the filtered catalog (<c>/learn?category=X</c>) with course counts and the copy's descriptions.</summary>
    private LinkListNode AcademySubjectsNode(AcademyOverview a)
    {
        var descriptions = _copy.Pairs("home.academy.subjects").ToDictionary(p => p.Title.ToLowerInvariant(), p => p.Text);
        return new LinkListNode(a.Categories.Select(c =>
        {
            var count = c.CourseCount == 1 ? "1 course" : $"{c.CourseCount} courses";
            var text = descriptions.TryGetValue(c.Category.ToString().ToLowerInvariant(), out var d) && d.Length > 0 ? $"{count}. {d}" : count;
            return new LinkItem(c.Label, "/learn?category=" + Uri.EscapeDataString(c.Category.ToString()), text);
        }).ToList());
    }

    /// <summary>Featured courses, then the others (the web app shows six).</summary>
    private static LinkListNode? FeaturedCoursesNode(AcademyOverview a, int limit = 6)
    {
        var picked = a.Featured.Concat(a.Courses.Where(c => !c.IsFeatured)).Take(limit).ToList();
        return picked.Count == 0 ? null : new LinkListNode(picked.Select(c => new LinkItem(c.Title, LearningLinks.CoursePath(c.Slug), c.Subtitle)).ToList());
    }

    private static readonly CourseLevel[] LevelOrder = { CourseLevel.Beginner, CourseLevel.Intermediate, CourseLevel.Advanced };

    /// <summary>Suggested paths (subjects named in <c>home.paths.items</c>): one course per level, featured first.</summary>
    private List<ContentNode> AcademyPathsNodes(AcademyOverview a)
    {
        var nodes = new List<ContentNode>();
        foreach (var (code, name) in _copy.Pairs("home.paths.items"))
        {
            var category = a.Categories.FirstOrDefault(c => c.Category.ToString().Equals(code, StringComparison.OrdinalIgnoreCase))?.Category;
            if (category is null) continue;
            var steps = LevelOrder
                .Select(level => a.Courses.Where(c => c.Category == category && c.Level == level).OrderByDescending(c => c.IsFeatured).FirstOrDefault())
                .OfType<CourseCardDto>().ToList();
            if (steps.Count < 2) continue;
            nodes.Add(new HeadingNode(3, name));
            nodes.Add(new ParagraphNode($"{steps.Count} courses · {steps.Sum(s => s.LessonCount)} lessons · {Minutes(steps.Sum(s => s.EstimatedMinutes))}"));
            nodes.Add(new LinkListNode(steps.Select(s => new LinkItem(s.Title, LearningLinks.CoursePath(s.Slug), s.Level.ToString())).ToList()));
        }
        return nodes;
    }

    /// <summary>"How it works" steps and the certificate strip.</summary>
    private List<ContentNode> LearnStepsAndCertificateNodes()
    {
        var nodes = new List<ContentNode>
        {
            new ParagraphNode(_copy.Text("home.learnSteps.eyebrow")),
            new HeadingNode(2, _copy.Text("home.learnSteps.title")),
        };
        foreach (var (title, text) in _copy.Pairs("home.learnSteps.steps"))
        {
            nodes.Add(new HeadingNode(3, title));
            nodes.Add(new ParagraphNode(text));
        }
        nodes.Add(new ParagraphNode(_copy.Text("home.cert.eyebrow")));
        nodes.Add(new HeadingNode(2, _copy.Text("home.cert.title")));
        nodes.Add(new ParagraphNode(_copy.Text("home.cert.text")));
        nodes.Add(new ListNode(_copy.List("home.cert.points")));
        nodes.Add(new ActionNode(_copy.Text("home.cert.cta"), "/learn"));
        return nodes;
    }

    private List<ContentNode> PathsSectionNodes(AcademyOverview a)
    {
        var paths = AcademyPathsNodes(a);
        if (paths.Count == 0) return paths;
        paths.InsertRange(0, new ContentNode[]
        {
            new ParagraphNode(_copy.Text("home.paths.eyebrow")),
            new HeadingNode(2, _copy.Text("home.paths.title")),
            new ParagraphNode(_copy.Text("home.paths.intro")),
        });
        return paths;
    }

    /// <summary>The home page's academy block: figures, subjects, featured courses, paths, steps and certificates.</summary>
    private async Task<List<ContentNode>> HomeAcademyNodesAsync(CancellationToken ct)
    {
        var a = await AcademyOverviewAsync(ct);
        var nodes = new List<ContentNode>
        {
            new ParagraphNode(_copy.Text("home.academy.eyebrow")),
            new HeadingNode(2, _copy.Text("home.academy.title")),
            new ParagraphNode(_copy.Text("home.academy.intro")),
            new ActionNode(_copy.Text("home.academy.cta"), "/learn"),
            AcademyStatsNode(a),
        };
        if (a.Categories.Count > 0)
        {
            nodes.Add(new HeadingNode(3, _copy.Text("home.academy.subjectsTitle")));
            nodes.Add(AcademySubjectsNode(a));
        }
        if (FeaturedCoursesNode(a) is { } featured)
        {
            nodes.Add(new HeadingNode(3, _copy.Text("home.academy.featuredTitle")));
            nodes.Add(featured);
        }
        nodes.AddRange(PathsSectionNodes(a));
        nodes.AddRange(LearnStepsAndCertificateNodes());
        return nodes;
    }

    /// <summary>The /academy overview's live part (after its CMS hero), in the web app's order.</summary>
    private async Task<List<ContentNode>> AcademyPageLiveNodesAsync(CancellationToken ct)
    {
        var a = await AcademyOverviewAsync(ct);
        var nodes = new List<ContentNode> { AcademyStatsNode(a) };
        if (a.Categories.Count > 0)
        {
            nodes.Add(new ParagraphNode(_copy.Text("home.academy.eyebrow")));
            nodes.Add(new HeadingNode(2, _copy.Text("home.academy.subjectsTitle")));
            nodes.Add(new ParagraphNode(_copy.Text("home.academy.intro")));
            nodes.Add(AcademySubjectsNode(a));
        }
        if (FeaturedCoursesNode(a) is { } featured)
        {
            nodes.Add(new HeadingNode(2, _copy.Text("home.academy.featuredTitle")));
            nodes.Add(new ActionNode(_copy.Text("home.academy.cta"), "/learn"));
            nodes.Add(featured);
        }
        nodes.AddRange(PathsSectionNodes(a));
        nodes.AddRange(LearnStepsAndCertificateNodes());
        return nodes;
    }
}
