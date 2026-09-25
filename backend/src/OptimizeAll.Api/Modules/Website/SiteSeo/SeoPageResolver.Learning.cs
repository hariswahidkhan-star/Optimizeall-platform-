using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Api.Modules.Website.Public;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Server-rendered academy pages (Learning module, docs/LEARNING.md): <c>/learn</c>, <c>/learn/{course}</c>,
/// <c>/learn/{course}/{lesson}</c> and <c>/verify/certificates/{id}</c>. Titles, descriptions, canonicals and JSON-LD
/// (Course, LearningResource/Article, VideoObject, BreadcrumbList, EducationalOccupationalCredential) come from
/// <see cref="PublicLearningService"/> / <see cref="CertificateService"/>, i.e. the same data as the web app's pages.
/// Unpublished courses, unknown lessons and unknown certificates are 404.
/// </summary>
public sealed partial class SeoPageResolver
{
    /// <summary>The academy home's title and description (identical to frontend/src/features/public/learn/AcademyPages.tsx).</summary>
    public const string AcademyTitle = "Free courses with certificates — sales, marketing, SEO, AI";
    public const string AcademyDescription =
        "Optimize All Academy: free, practical courses in sales, marketing, SEO and AI. Learn at your own pace and add a verified certificate to LinkedIn.";

    private const string LearningAdminPath = "/admin/learning";

    private void ApplyLearningSeo(SeoPage page, LearningSeoDto seo, IEnumerable<System.Text.Json.JsonElement> jsonLd, string? imageAlt) =>
        ApplySeo(page, new PublicSeoDto(seo.Title, seo.Description, seo.ImageUrl, _ld.Url(seo.CanonicalPath), seo.NoIndex), jsonLd, imageAlt);

    private async Task<SeoPage> AcademyAsync(CancellationToken ct)
    {
        var page = NewPage("/learn", AcademyTitle, AcademyDescription);
        page.Source = "Academy";
        page.EditPath = LearningAdminPath;
        CrumbsLd(page, ("Academy", "/learn"));
        var courses = await learning.CatalogAsync(new CatalogQuery { Page = 1, PageSize = 200 }, ct);
        page.ModifiedAt = courses.Items.Select(c => c.PublishedAt).Max();
        var c = page.Content;
        c.Add(new ParagraphNode("Optimize All Academy"));
        c.Add(new HeadingNode(1, "Free courses. Real skills. Verified certificates."));
        c.Add(new ParagraphNode("Practical training in sales, marketing, SEO and AI — built for creators, freelancers and growing teams. " +
                                "Read every lesson for free; create a free account to track progress, take the final assessment and earn a " +
                                "certificate you can add to LinkedIn."));
        var paths = await learning.Paths.ListAsync(ct);
        if (paths.Paths.Count > 0)
        {
            c.Add(new HeadingNode(2, "Learning paths"));
            c.Add(new LinkListNode(paths.Paths.Select(p => new LinkItem($"{p.Title} learning path", LearningLinks.PathPath(p.Slug),
                $"{p.Subtitle} · {p.CourseCount} courses")).ToList()));
        }
        foreach (var group in courses.Items.GroupBy(x => x.Category).OrderBy(g => g.Key))
        {
            c.Add(new HeadingNode(2, PublicLearningService.CategoryLabels[group.Key]));
            c.Add(new LinkListNode(group.Select(x => new LinkItem(x.Title, LearningLinks.CoursePath(x.Slug), x.Subtitle)).ToList()));
        }
        return page;
    }

    /// <summary>"Sep 2026" from a pack's lastReviewed ("2026-09"), or null.</summary>
    public static string? ReviewedLabel(string? lastReviewed) =>
        lastReviewed is not null && DateTime.TryParseExact(lastReviewed + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var month)
            ? month.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>/learn/paths: every learning path with its courses.</summary>
    private async Task<SeoPage> PathsIndexAsync(CancellationToken ct)
    {
        var d = await learning.Paths.ListAsync(ct);
        var page = NewPage(LearningLinks.PathsPath, d.Seo.Title, d.Seo.Description);
        page.Source = "Learning paths";
        page.EditPath = LearningAdminPath;
        ApplyLearningSeo(page, d.Seo, d.JsonLd, null);
        Crumbs(page, ("Academy", "/learn"), ("Learning paths", LearningLinks.PathsPath));
        var c = page.Content;
        c.Add(new ParagraphNode("Optimize All Academy"));
        c.Add(new HeadingNode(1, "Learning paths"));
        c.Add(new ParagraphNode("Step-by-step tracks of free courses towards a role or goal. Every course ends with a verifiable certificate and a badge you can add to LinkedIn."));
        foreach (var p in d.Paths)
        {
            c.Add(new HeadingNode(2, p.Title));
            c.Add(new ParagraphNode($"{p.Subtitle} · {p.Level} · {p.CourseCount} courses · about {Math.Max(1, (int)Math.Round(p.TotalMinutes / 60.0))} hours"));
            c.Add(new LinkListNode(new[] { new LinkItem($"View the {p.Title} path", LearningLinks.PathPath(p.Slug)) }));
        }
        return page;
    }

    /// <summary>/learn/paths/{slug}: the path's description, outcomes, audience and ordered courses.</summary>
    private async Task<SeoPage> PathAsync(string slug, CancellationToken ct)
    {
        var d = await learning.Paths.DetailAsync(slug, ct); // unknown or no published course → 404
        var path = LearningLinks.PathPath(d.Card.Slug);
        var page = NewPage(path, d.Seo.Title, d.Seo.Description);
        page.Source = "Learning path";
        page.EditPath = LearningAdminPath;
        ApplyLearningSeo(page, d.Seo, d.JsonLd, null);
        Crumbs(page, ("Academy", "/learn"), ("Learning paths", LearningLinks.PathsPath), (d.Card.Title, path));
        var c = page.Content;
        c.Add(new ParagraphNode($"Learning path · {d.Card.Level} · {d.Card.CourseCount} free courses · about {Math.Max(1, (int)Math.Round(d.Card.TotalMinutes / 60.0))} hours"));
        c.Add(new HeadingNode(1, $"{d.Card.Title} learning path"));
        c.Add(new ParagraphNode(d.Card.Subtitle));
        if (d.Courses.Count > 0) c.Add(new ActionNode("Start the first course", LearningLinks.CoursePath(d.Courses[0].Course.Slug)));
        c.Add(new HeadingNode(2, "About this path"));
        c.Add(new MarkdownNode(d.Description, 3));
        if (d.Outcomes.Count > 0)
        {
            c.Add(new HeadingNode(2, "What you will be able to do"));
            c.Add(new ListNode(d.Outcomes));
        }
        if (d.Audience.Count > 0)
        {
            c.Add(new HeadingNode(2, "Who it is for"));
            c.Add(new ListNode(d.Audience));
        }
        c.Add(new HeadingNode(2, "Courses in order"));
        c.Add(new LinkListNode(d.Courses.Select(x => new LinkItem($"{x.Position}. {x.Course.Title}", LearningLinks.CoursePath(x.Course.Slug),
            $"{x.Course.Subtitle} · {x.Course.LessonCount} lessons · badge: {x.Course.BadgeName}")).ToList()));
        c.Add(new HeadingNode(2, "Badges you earn along the way"));
        c.Add(new ListNode(d.Card.Badges.Select(b => $"{b.BadgeName} — {b.CourseTitle}").ToList()));
        return page;
    }

    private async Task<SeoPage> CourseAsync(string slug, CancellationToken ct)
    {
        if (slug == "paths") return await PathsIndexAsync(ct); // /learn/paths (a reserved course slug)
        var d = await learning.CourseAsync(slug, ct); // unpublished or unknown → 404
        var path = LearningLinks.CoursePath(d.Card.Slug);
        var page = NewPage(path, d.Seo.Title, d.Seo.Description);
        page.Source = "Course";
        page.EditPath = LearningAdminPath;
        ApplyLearningSeo(page, d.Seo, d.JsonLd, d.Card.BadgeName);
        Crumbs(page, ("Academy", "/learn"), (d.Card.Title, path));
        page.ModifiedAt = d.UpdatedAt;
        page.Section = PublicLearningService.CategoryLabels[d.Card.Category];
        var c = page.Content;
        var reviewed = ReviewedLabel(d.LastReviewed);
        c.Add(new ParagraphNode($"{PublicLearningService.CategoryLabels[d.Card.Category]} · {d.Card.Level} · {d.Card.EstimatedMinutes} minutes · free" +
                                (reviewed is null ? string.Empty : $" · updated {reviewed}")));
        c.Add(new HeadingNode(1, d.Card.Title));
        c.Add(new ParagraphNode(d.Card.Subtitle));
        var facts = new List<KeyValuePair<string, string>>
        {
            KeyValuePair.Create("Lessons", $"{d.Card.LessonCount} in {d.Card.ModuleCount} modules"),
        };
        if (d.LectureCount > 0) facts.Add(KeyValuePair.Create("Video lectures", $"{d.LectureCount} lectures, {d.LectureMinutes} minutes"));
        if (reviewed is not null) facts.Add(KeyValuePair.Create("Updated", reviewed));
        c.Add(new FactsNode(facts));
        if (d.Tools.Count > 0)
        {
            c.Add(new HeadingNode(2, "Tools you'll use"));
            c.Add(new ListNode(d.Tools));
        }
        var first = d.Modules.SelectMany(m => m.Lessons).FirstOrDefault();
        if (first is not null) c.Add(new ActionNode("Start the course", LearningLinks.LessonPath(d.Card.Slug, first.Slug)));
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            c.Add(new HeadingNode(2, "About this course"));
            c.Add(new MarkdownNode(d.Description, 3));
        }
        if (d.Outcomes.Count > 0)
        {
            c.Add(new HeadingNode(2, "What you will learn"));
            c.Add(new ListNode(d.Outcomes));
        }
        if (d.Prerequisites.Count > 0)
        {
            c.Add(new HeadingNode(2, "Before you start"));
            c.Add(new LinkListNode(d.Prerequisites.Select(p => new LinkItem(p.Title, LearningLinks.CoursePath(p.Slug))).ToList()));
        }
        c.Add(new HeadingNode(2, "Course content"));
        foreach (var m in d.Modules)
        {
            c.Add(new HeadingNode(3, m.Title));
            if (!string.IsNullOrWhiteSpace(m.Summary)) c.Add(new ParagraphNode(m.Summary));
            c.Add(new LinkListNode(m.Lessons.Select(l => new LinkItem(l.Title, LearningLinks.LessonPath(d.Card.Slug, l.Slug), $"{l.DurationMinutes} min")).ToList()));
        }
        c.Add(new HeadingNode(2, $"Certificate: {d.Badge.Name}"));
        c.Add(new ParagraphNode(d.Badge.Description));
        c.Add(new FactsNode(new[]
        {
            KeyValuePair.Create("Final assessment", $"{d.Exam.QuestionCount} questions, {d.Exam.TimeLimitMinutes} minutes"),
            KeyValuePair.Create("Passing score", $"{d.Exam.PassingScore}%"),
        }));
        return page;
    }

    private async Task<SeoPage> LessonAsync(string slug, string lessonSlug, CancellationToken ct)
    {
        if (slug == "paths") return await PathAsync(lessonSlug, ct); // /learn/paths/{path}
        var l = await learning.LessonAsync(slug, lessonSlug, ct); // unknown course or lesson → 404
        var path = LearningLinks.LessonPath(l.CourseSlug, l.Slug);
        var page = NewPage(path, l.Seo.Title, l.Seo.Description);
        page.Source = "Lesson";
        page.EditPath = LearningAdminPath;
        page.OgType = "article";
        ApplyLearningSeo(page, l.Seo, l.JsonLd, l.CourseTitle);
        Crumbs(page, ("Academy", "/learn"), (l.CourseTitle, LearningLinks.CoursePath(l.CourseSlug)), (l.Title, path));
        page.Section = PublicLearningService.CategoryLabels[l.Category];
        var c = page.Content;
        c.Add(new ParagraphNode($"{l.CourseTitle} · {l.ModuleTitle} · lesson {l.Position} of {l.LessonCount} · {l.DurationMinutes} min"));
        c.Add(new HeadingNode(1, l.Title));
        c.Add(new MarkdownNode(l.Body, 2));
        if (l.Lecture is { } lecture)
        {
            // The lecture transcript is crawlable text on the page, produced or not (the web app shows it in a panel).
            c.Add(new HeadingNode(2, $"Video lecture: {lecture.Title}"));
            c.Add(new ParagraphNode(lecture.Produced
                ? $"{lecture.Chapters.Count} chapters · about {lecture.TargetMinutes} minutes · captions and full transcript below."
                : $"Lecture coming soon · {lecture.Chapters.Count} chapters · about {lecture.TargetMinutes} minutes. Read the full transcript below."));
            c.Add(new ListNode(lecture.Chapters.Select(ch => ch.Title).ToList(), Ordered: true));
            c.Add(new HeadingNode(2, "Lecture transcript"));
            foreach (var chapter in lecture.Chapters)
            {
                c.Add(new HeadingNode(3, chapter.Title));
                c.Add(new ParagraphNode(chapter.Narration));
            }
        }
        if (l.Video is { Transcript.Length: > 0 } video)
        {
            c.Add(new HeadingNode(2, "Video transcript"));
            c.Add(new MarkdownNode(video.Transcript, 3));
        }
        if (l.KeyTakeaways.Count > 0)
        {
            c.Add(new HeadingNode(2, "Key takeaways"));
            c.Add(new ListNode(l.KeyTakeaways));
        }
        if (!string.IsNullOrWhiteSpace(l.Activity))
        {
            c.Add(new HeadingNode(2, "Try it"));
            c.Add(new MarkdownNode(l.Activity, 3));
        }
        var nav = new List<LinkItem>();
        if (l.Previous is { } prev) nav.Add(new LinkItem($"Previous: {prev.Title}", LearningLinks.LessonPath(l.CourseSlug, prev.Slug)));
        if (l.Next is { } next) nav.Add(new LinkItem($"Next: {next.Title}", LearningLinks.LessonPath(l.CourseSlug, next.Slug)));
        nav.Add(new LinkItem($"All lessons of {l.CourseTitle}", LearningLinks.CoursePath(l.CourseSlug)));
        c.Add(new LinkListNode(nav));
        return page;
    }

    private async Task<SeoPage?> CertificateAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var certificateId)) return null;
        var v = await certificates.VerifyAsync(certificateId, ct); // unknown → 404
        var path = LearningLinks.VerifyPath(v.Id);
        var page = NewPage(path, v.Seo.Title, v.Seo.Description);
        page.Source = "Certificate";
        page.EditPath = LearningAdminPath;
        ApplyLearningSeo(page, v.Seo, v.JsonLd, v.BadgeName);
        Crumbs(page, ("Academy", "/learn"), ("Certificate verification", path));
        var c = page.Content;
        c.Add(new ParagraphNode("Certificate verification"));
        c.Add(new HeadingNode(1, v.IsValid ? $"{v.HolderName} — {v.BadgeName}" : $"Revoked certificate {v.VerificationCode}"));
        c.Add(new ParagraphNode(v.IsValid
            ? $"Valid certificate: {v.HolderName} earned it by completing \"{v.CourseTitle}\" and passing its final assessment."
            : "Revoked certificate: it is no longer valid."));
        var facts = new List<KeyValuePair<string, string>>
        {
            KeyValuePair.Create("Status", v.IsValid ? "Valid" : "Revoked"),
            KeyValuePair.Create("Verification code", v.VerificationCode),
            KeyValuePair.Create("Issued by", v.IssuerName),
            KeyValuePair.Create("Issued on", v.IssuedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (v.RevokedAt is { } revoked) facts.Add(KeyValuePair.Create("Revoked on", revoked.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        c.Add(new FactsNode(facts));
        if (v.Skills.Count > 0)
        {
            c.Add(new HeadingNode(2, "Skills"));
            c.Add(new ListNode(v.Skills));
        }
        c.Add(new LinkListNode(new[] { new LinkItem($"About the course: {v.CourseTitle}", LearningLinks.CoursePath(v.CourseSlug)) }));
        return page;
    }
}
