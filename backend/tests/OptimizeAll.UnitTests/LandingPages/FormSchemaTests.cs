using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Domain.LandingPages;
using Field = OptimizeAll.Domain.LandingPages.FormField;

namespace OptimizeAll.UnitTests.LandingPages;

public sealed class FormSchemaTests
{
    private static FormSchema Schema(params Field[] fields) => new() { Steps = { new FormStep { Id = "one", Fields = fields.ToList() } } };

    private static readonly FormSchema Quote = Schema(
        new Field { Key = "name", Type = "text", Label = "Name", Required = true },
        new Field { Key = "email", Type = "email", Label = "Email", Required = true },
        new Field { Key = "budget", Type = "select", Label = "Budget", Required = true, Options = new() { new() { Value = "small", Label = "Small" }, new() { Value = "other", Label = "Other" } } },
        new Field { Key = "budget_other", Type = "number", Label = "Your budget", Required = true, Validation = new FieldValidation { Min = 100, Max = 100000 },
            ShowIf = new FieldCondition { Field = "budget", Operator = "equals", Value = "other" } },
        new Field { Key = "details", Type = "textarea", Label = "Details", Required = true,
            ShowIf = new FieldCondition { Field = "budget_other", Operator = "greaterThan", Value = "5000" } },
        new Field { Key = "channels", Type = "multiselect", Label = "Channels", Options = new() { new() { Value = "seo", Label = "SEO" }, new() { Value = "ads", Label = "Ads" } },
            Validation = new FieldValidation { MaxChoices = 1 } },
        new Field { Key = "phone", Type = "phone", Label = "Phone" },
        new Field { Key = "start", Type = "date", Label = "Start" },
        new Field { Key = "utm_source", Type = "hidden", Label = "", UrlParam = "utm_source" },
        new Field { Key = "consent", Type = "consent", Label = "Consent", Required = true });

    private static Dictionary<string, IReadOnlyList<string>> Values(params (string Key, string[] Values)[] entries) =>
        entries.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Values);

    [Fact]
    public void Valid_submission_is_normalized()
    {
        var result = FormSchemas.ValidateSubmission(Quote, Values(("name", new[] { " Ada " }), ("email", new[] { "ada@example.com" }), ("budget", new[] { "small" }),
            ("channels", new[] { "seo" }), ("consent", new[] { "true" }), ("utm_source", new[] { "google" }), ("unknown", new[] { "ignored" })), Array.Empty<UploadedFile>());
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Key}: {string.Join(",", e.Value)}")));
        Assert.Equal("Ada", result.Values["name"]);
        Assert.Equal("yes", result.Values["consent"]);
        Assert.Equal("google", result.Values["utm_source"]);
        Assert.True(result.ConsentGiven);
        Assert.False(result.Values.ContainsKey("unknown"));
    }

    [Fact]
    public void Conditional_fields_are_required_only_when_shown_and_dropped_when_hidden()
    {
        // Hidden: budget != other → budget_other not required and its value is dropped.
        var hidden = FormSchemas.ValidateSubmission(Quote, Values(("name", new[] { "A" }), ("email", new[] { "a@b.co" }), ("budget", new[] { "small" }),
            ("budget_other", new[] { "999999" }), ("consent", new[] { "yes" })), Array.Empty<UploadedFile>());
        Assert.True(hidden.IsValid);
        Assert.False(hidden.Values.ContainsKey("budget_other"));

        // Shown: required and validated (range), and a chained condition (details shown when budget_other > 5000).
        var shown = FormSchemas.ValidateSubmission(Quote, Values(("name", new[] { "A" }), ("email", new[] { "a@b.co" }), ("budget", new[] { "other" }),
            ("consent", new[] { "yes" })), Array.Empty<UploadedFile>());
        Assert.Contains("budget_other", shown.Errors.Keys);
        Assert.DoesNotContain("details", shown.Errors.Keys);

        var chained = FormSchemas.ValidateSubmission(Quote, Values(("name", new[] { "A" }), ("email", new[] { "a@b.co" }), ("budget", new[] { "other" }),
            ("budget_other", new[] { "8000" }), ("consent", new[] { "yes" })), Array.Empty<UploadedFile>());
        Assert.Contains("details", chained.Errors.Keys);

        // The chain collapses when the first condition turns off, even if a stale value remains.
        var collapsed = FormSchemas.ValidateSubmission(Quote, Values(("name", new[] { "A" }), ("email", new[] { "a@b.co" }), ("budget", new[] { "small" }),
            ("budget_other", new[] { "8000" }), ("consent", new[] { "yes" })), Array.Empty<UploadedFile>());
        Assert.True(collapsed.IsValid);
    }

    [Fact]
    public void Field_types_are_validated_server_side()
    {
        var result = FormSchemas.ValidateSubmission(Quote, Values(("email", new[] { "not-an-email" }), ("budget", new[] { "hacked" }),
            ("channels", new[] { "seo", "ads" }), ("phone", new[] { "12" }), ("start", new[] { "31/12/2026" }), ("consent", new[] { "false" })), Array.Empty<UploadedFile>());
        Assert.Equal(new[] { "budget", "channels", "consent", "email", "name", "phone", "start" }, result.Errors.Keys.OrderBy(k => k));
        Assert.False(result.ConsentGiven);
    }

    [Fact]
    public void Regex_patterns_run_with_a_timeout()
    {
        var schema = Schema(new Field { Key = "code", Type = "text", Label = "Code", Required = true, Validation = new FieldValidation { Pattern = "(a+)+$", PatternMessage = "Bad code" } });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = FormSchemas.ValidateSubmission(schema, Values(("code", new[] { new string('a', 40) + "!" })), Array.Empty<UploadedFile>());
        Assert.Contains("Bad code", result.Errors["code"]);
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }

    [Fact]
    public void File_fields_accept_real_pdfs_and_images_only()
    {
        var schema = Schema(
            new Field { Key = "cv", Type = "file", Label = "CV", Required = true, Validation = new FieldValidation { Accept = new() { "pdf" }, MaxSizeMb = 1 } },
            new Field { Key = "photo", Type = "file", Label = "Photo", Validation = new FieldValidation { Accept = new() { "image" } } });
        var pdf = "%PDF-1.7\n1 0 obj<<>>endobj\n"u8.ToArray();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 0, 10, 0, 0, 0, 10, 8, 2, 0, 0, 0 };
        var exe = "MZ\x90\0 this is a program"u8.ToArray();

        var ok = FormSchemas.ValidateSubmission(schema, Values(), new[] { new UploadedFile("cv", "../../etc/My CV.pdf", pdf), new UploadedFile("photo", "me.png", png) });
        Assert.True(ok.IsValid);
        Assert.Equal("My-CV.pdf", ok.Files.Single(f => f.FieldKey == "cv").FileName);
        Assert.Equal("image/png", ok.Files.Single(f => f.FieldKey == "photo").ContentType);

        var renamed = FormSchemas.ValidateSubmission(schema, Values(), new[] { new UploadedFile("cv", "cv.pdf", exe) });
        Assert.Contains("cv", renamed.Errors.Keys);
        var wrongKind = FormSchemas.ValidateSubmission(schema, Values(), new[] { new UploadedFile("cv", "cv.png", png) });
        Assert.Contains("cv", wrongKind.Errors.Keys);
        var tooBig = FormSchemas.ValidateSubmission(schema, Values(), new[] { new UploadedFile("cv", "cv.pdf", pdf.Concat(new byte[1024 * 1024]).ToArray()) });
        Assert.Contains("at most 1 MB", tooBig.Errors["cv"][0]);
        var missing = FormSchemas.ValidateSubmission(schema, Values(), Array.Empty<UploadedFile>());
        Assert.Contains("cv", missing.Errors.Keys);
    }

    [Fact]
    public void Schema_validation_catches_bad_definitions()
    {
        var bad = Schema(
            new Field { Key = "a", Type = "select", Label = "A" },
            new Field { Key = "a", Type = "rocket", Label = "Dup" },
            new Field { Key = "b", Type = "text", Label = "B", ShowIf = new FieldCondition { Field = "later", Operator = "equals", Value = "x" } },
            new Field { Key = "later", Type = "text", Label = "L", Validation = new FieldValidation { Pattern = "(" } },
            new Field { Key = "h", Type = "hidden", Label = "", Required = true, UrlParam = "password" });
        var errors = FormSchemas.ValidateSchema(bad);
        Assert.Contains(errors.Keys, k => k.EndsWith("fields[0].options", StringComparison.Ordinal));
        Assert.Contains(errors.Keys, k => k.EndsWith("fields[1].key", StringComparison.Ordinal));
        Assert.Contains(errors.Keys, k => k.EndsWith("fields[2].showIf.field", StringComparison.Ordinal));
        Assert.Contains(errors.Keys, k => k.EndsWith("fields[3].validation.pattern", StringComparison.Ordinal));
        Assert.Contains(errors.Keys, k => k.EndsWith("fields[4].urlParam", StringComparison.Ordinal));
        Assert.Empty(FormSchemas.ValidateSchema(Quote));
    }

    [Fact]
    public void Seeded_form_templates_are_valid()
    {
        Assert.Equal(new[] { "contact", "quote", "newsletter", "webinar-registration" }, TemplateCatalog.Forms.Select(f => f.Key));
        foreach (var template in TemplateCatalog.Forms)
            Assert.Empty(FormSchemas.ValidateSchema(FormSchemas.Deserialize(template.SchemaJson)));
    }

    [Theory]
    [InlineData("https://Example.com/", "https://example.com")]
    [InlineData("https://shop.example.com:8443", "https://shop.example.com:8443")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("http://example.com", null)]
    [InlineData("https://example.com/path", null)]
    [InlineData("javascript:alert(1)", null)]
    public void Origins_are_normalized_strictly(string input, string? expected) => Assert.Equal(expected, FormService.NormalizeOrigin(input));

    [Fact]
    public void Autoresponder_merge_fills_placeholders_as_plain_text()
    {
        var form = new Form { Name = "Quote" };
        Assert.Equal("Hi Ada, thanks for the Quote request. ", FormSubmissionService.Merge("Hi {{name}}, thanks for the {{form}} request. {{unknown}}",
            new Dictionary<string, string> { ["name"] = "Ada" }, form));
        Assert.Equal("Hi there,", FormSubmissionService.Merge("Hi {{first_name}},", new Dictionary<string, string>(), form));
    }
}
