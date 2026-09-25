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
        foreach (var group in courses.Items.GroupBy(x => x.Category).OrderBy(g => g.Key))
        {
            c.Add(new HeadingNode(2, PublicLearningService.CategoryLabels[group.Key]));
            c.Add(new LinkListNode(group.Select(x => new LinkItem(x.Title, LearningLinks.CoursePath(x.Slug), x.Subtitle)).ToList()));
        }
        return page;
    }

    private async Task<SeoPage> CourseAsync(string slug, CancellationToken ct)
    {
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
        c.Add(new ParagraphNode($"{PublicLearningService.CategoryLabels[d.Card.Category]} · {d.Card.Level} · {d.Card.EstimatedMinutes} minutes · free"));
        c.Add(new HeadingNode(1, d.Card.Title));
        c.Add(new ParagraphNode(d.Card.Subtitle));
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
