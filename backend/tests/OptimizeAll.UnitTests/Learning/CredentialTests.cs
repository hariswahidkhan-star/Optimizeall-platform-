using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Json.Schema;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.UnitTests.Learning;

public sealed class CredentialTests
{
    private static readonly LearningLinks Links = new("https://optimizeall.example/");
    private static CoursePack Pack => CoursePackLibrary.All.Single(f => f.FileName == "platform-getting-started.json").Pack!;

    private static Certificate Cert(bool revoked = false) => new()
    {
        Id = Guid.Parse("0197a3b4-1c2d-7e3f-8a9b-0c1d2e3f4a5b"),
        UserId = Guid.NewGuid(), CourseId = Guid.NewGuid(), CourseVersionId = Guid.NewGuid(),
        VerificationCode = "OA-ABCD-2345", HolderName = "Amina <Khan> & Co", CourseSlug = "platform-getting-started",
        CourseTitle = "Getting started on Optimize All", BadgeName = "Optimize All Certified Creator",
        Skills = new() { "Creator campaigns", "Advertising disclosure" }, Score = 90,
        IssuedAt = new DateTime(2026, 9, 25, 10, 30, 0, DateTimeKind.Utc), RecipientSalt = "0011223344556677",
        RevokedAt = revoked ? new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc) : null,
    };

    [Fact]
    public void LinkedIn_add_to_profile_url_uses_the_documented_parameters()
    {
        var issuer = new LearningIssuer("Optimize All Academy", null, "https://optimizeall.example");
        var url = new Uri(LinkedIn.AddToProfile("Certified Creator & Co", issuer, new DateTime(2026, 9, 25), "https://optimizeall.example/verify/certificates/x", "OA-ABCD-2345"));
        Assert.Equal("https://www.linkedin.com/profile/add", url.GetLeftPart(UriPartial.Path));
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("CERTIFICATION_NAME", q["startTask"]);
        Assert.Equal("Certified Creator & Co", q["name"]);
        Assert.Equal("Optimize All Academy", q["organizationName"]);
        Assert.Null(q["organizationId"]);
        Assert.Equal("2026", q["issueYear"]);
        Assert.Equal("9", q["issueMonth"]);
        Assert.Equal("https://optimizeall.example/verify/certificates/x", q["certUrl"]);
        Assert.Equal("OA-ABCD-2345", q["certId"]);

        var withId = new Uri(LinkedIn.AddToProfile("X", issuer with { LinkedInOrganizationId = "1337" }, new DateTime(2026, 1, 2), "https://a/b", "c"));
        var q2 = System.Web.HttpUtility.ParseQueryString(withId.Query);
        Assert.Equal("1337", q2["organizationId"]);
        Assert.Null(q2["organizationName"]);

        Assert.Equal("https://www.linkedin.com/sharing/share-offsite/?url=https%3A%2F%2Foptimizeall.example%2Fverify%2Fcertificates%2Fx",
            LinkedIn.Share("https://optimizeall.example/verify/certificates/x"));
    }

    private static EvaluationResults Validate(string schema, JsonNode document)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Learning", "OpenBadgesSchemas", schema);
        var json = JsonSchema.FromText(File.ReadAllText(path));
        return json.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
    }

    private static string Errors(EvaluationResults r) =>
        string.Join("; ", (r.Details ?? Array.Empty<EvaluationResults>()).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")));

    [Fact]
    public void Open_badges_documents_validate_against_the_OB_2_0_schemas()
    {
        var issuer = new LearningIssuer("Optimize All Academy", null, "https://optimizeall.example");
        var profile = OpenBadges.Issuer(issuer, Links, "hello@optimizeall.example");
        var r = Validate("profile.schema.json", profile);
        Assert.True(r.IsValid, Errors(r));

        var badge = OpenBadges.BadgeClass(Pack, Links);
        r = Validate("badgeclass.schema.json", badge);
        Assert.True(r.IsValid, Errors(r));
        Assert.Equal(profile["id"]!.GetValue<string>(), badge["issuer"]!.GetValue<string>());

        var assertion = OpenBadges.Assertion(Cert(), "Amina@Example.com", Links);
        r = Validate("assertion.schema.json", assertion);
        Assert.True(r.IsValid, Errors(r));
        Assert.Equal(badge["id"]!.GetValue<string>(), assertion["badge"]!.GetValue<string>());
        Assert.Equal(OpenBadges.HashIdentity("amina@example.com", "0011223344556677"), assertion["recipient"]!["identity"]!.GetValue<string>());
        Assert.DoesNotContain("amina@example.com", assertion.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        // A missing required property fails validation (the schema is doing its job).
        assertion.Remove("verification");
        Assert.False(Validate("assertion.schema.json", assertion).IsValid);
    }

    [Fact]
    public void Svg_artwork_is_well_formed_and_escapes_text()
    {
        var badge = CertificateArt.BadgeSvg("Growth <Hacker> & \"AI\"", CourseCategory.Ai, CourseLevel.Advanced, "Academy & Co");
        var doc = XDocument.Parse(badge);
        Assert.Equal("svg", doc.Root!.Name.LocalName);
        Assert.DoesNotContain("<Hacker>", badge);

        var text = new CertificateArt.CertificateText("Amina <Khan> & Co", "Course <script>alert(1)</script>", "Badge", CourseCategory.Sales,
            CourseLevel.Beginner, new[] { "Skill & more" }, DateTime.UtcNow, "OA-ABCD-2345", "https://x.example/verify/certificates/1", "Academy", Revoked: true);
        var svg = CertificateArt.CertificateSvg(text);
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("REVOKED", svg);
        Assert.Contains("OA-ABCD-2345", svg);
    }

    [Fact]
    public void Certificate_pdf_renders_with_embedded_fonts_and_metadata()
    {
        var text = new CertificateArt.CertificateText("Zoë Ångström-Øberg", "Getting started on Optimize All", "Optimize All Certified Creator",
            CourseCategory.Platform, CourseLevel.Beginner, new[] { "Creator campaigns", "Advertising disclosure" },
            new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), "OA-ABCD-2345", "https://optimizeall.example/verify/certificates/1", "Optimize All Academy", false);
        var pdf = CertificatePdf.Render(text);
        Assert.True(pdf.Length > 10_000);
        var raw = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-", raw);
        Assert.Contains("%%EOF", raw[^32..]);
        Assert.Contains("/FontFile2", raw); // TrueType fonts embedded (no system fonts needed)
        var fonts = System.Text.RegularExpressions.Regex.Matches(raw, @"/BaseFont\s*/([^\s/>]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(fonts.Any(f => f.Contains("Work", StringComparison.OrdinalIgnoreCase)) && fonts.Any(f => f.Contains("Lora", StringComparison.OrdinalIgnoreCase)),
            "Fonts: " + string.Join(", ", fonts));
        Assert.Contains("OA-ABCD-2345", raw); // document subject
        // Renders twice in one process (the font resolver is installed once).
        Assert.True(CertificatePdf.Render(text with { Revoked = true }).Length > 10_000);
    }

    [Fact]
    public void Verification_codes_are_well_formed_and_random()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => CertificateService.NewVerificationCode()).ToList();
        Assert.All(codes, c => Assert.Matches("^OA-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", c));
        Assert.Equal(500, codes.Distinct().Count());
    }

    [Fact]
    public void The_server_rendered_verification_page_encodes_everything()
    {
        var issuer = new LearningIssuer("Academy", null, "https://optimizeall.example");
        var c = Cert();
        var dto = new CertificateVerificationDto(c.Id, c.VerificationCode, "valid", true, c.HolderName, c.CourseSlug, c.CourseTitle, c.BadgeName,
            "Desc </script><script>alert(1)</script>", "Criteria", c.Skills, c.IssuedAt, null, issuer.Name, Links.For(c, issuer),
            new LearningSeoDto("Title <b>", "Desc", "/verify/certificates/x", null, false),
            new[] { LearningJsonLd.Credential(c, Links, issuer) });
        var html = VerificationPage.Render(dto, issuer.BaseUrl);
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("Amina <Khan>", html);
        Assert.Contains("Amina &lt;Khan&gt; &amp; Co", html);
        Assert.Contains("<meta property=\"og:url\" content=\"https://optimizeall.example/verify/certificates/", html);
        Assert.Contains("application/ld+json", html);
    }
}
