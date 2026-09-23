using OptimizeAll.Api.Modules.Crm;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Crm;

namespace OptimizeAll.UnitTests.Crm;

public sealed class CrmRuleTests
{
    private static LeadScoringRule Fit(string field, string match, int points) =>
        new() { Name = $"{field}={match}", Category = ScoringCategory.Fit, Field = field, MatchValue = match, Points = points };

    private static LeadScoringRule Engagement(string type, int points, int? max) =>
        new() { Name = type, Category = ScoringCategory.Engagement, Field = type, Points = points, MaxOccurrences = max };

    [Fact]
    public void Scores_fit_and_capped_engagement()
    {
        var rules = new[]
        {
            Fit("industry", "saas, e-commerce", 15),
            Fit("companySize", "Medium,Large", 10),
            Fit("budgetRange", "*", 5),
            Fit("country", "GB", 7),
            Engagement("form_submitted", 10, 2),
            Engagement("email_clicked", 3, null),
            new LeadScoringRule { Name = "inactive", Category = ScoringCategory.Fit, Field = "industry", MatchValue = "*", Points = 100, IsActive = false },
        };
        var facts = new ScoringFacts("SaaS", "Medium", "5k-10k", "US", "Referral", "Lead",
            new Dictionary<string, int> { ["form_submitted"] = 5, ["email_clicked"] = 4 });
        var result = LeadScoring.Evaluate(rules, facts);
        Assert.Equal(15 + 10 + 5 + 20 + 12, result.Score);
        Assert.DoesNotContain(result.Lines, l => l.Rule == "inactive");
    }

    [Fact]
    public void Score_is_clamped_between_zero_and_max()
    {
        var facts = new ScoringFacts("x", null, null, null, null, null, new Dictionary<string, int>());
        Assert.Equal(0, LeadScoring.Evaluate(new[] { Fit("industry", "x", -50) }, facts).Score);
        Assert.Equal(LeadScoring.MaxScore, LeadScoring.Evaluate(new[] { Fit("industry", "x", 5000) }, facts).Score);
    }

    [Theory]
    [InlineData(ScoringCategory.Fit, "industry", true)]
    [InlineData(ScoringCategory.Fit, "favouriteColour", false)]
    [InlineData(ScoringCategory.Engagement, "webinar_attended", true)]
    [InlineData(ScoringCategory.Engagement, "Bad Type", false)]
    public void Validates_rule_fields(ScoringCategory category, string field, bool valid) =>
        Assert.Equal(valid, LeadScoring.IsValidField(category, field));

    [Fact]
    public void Custom_fields_are_validated_and_normalized()
    {
        Assert.Equal("{}", CustomFields.Normalize(null));
        Assert.Equal("{\"a\":1,\"b\":\"x\",\"c\":true}", CustomFields.Normalize("{\"c\":true,\"b\":\"x\",\"a\":1}"));
        Assert.Equal("crm.invalid_custom_fields", Assert.Throws<DomainException>(() => CustomFields.Normalize("[1,2]")).Code);
        Assert.Equal("crm.invalid_custom_fields", Assert.Throws<DomainException>(() => CustomFields.Normalize("{\"a\":{\"nested\":1}}")).Code);
        Assert.Equal("crm.invalid_custom_fields", Assert.Throws<DomainException>(() => CustomFields.Normalize("{\"bad$key\":1}")).Code);
        Assert.Equal("crm.invalid_custom_fields", Assert.Throws<DomainException>(() => CustomFields.Normalize("not json")).Code);
    }

    [Theory]
    [InlineData("https://www.Example.com/path", "example.com")]
    [InlineData("example.co.uk", "example.co.uk")]
    [InlineData("not a domain", null)]
    [InlineData("", null)]
    public void Normalizes_domains(string input, string? expected) => Assert.Equal(expected, CrmNormalization.Domain(input));

    [Fact]
    public void Free_mail_domains_are_not_company_domains()
    {
        Assert.Null(CrmNormalization.EmailDomain("someone@gmail.com"));
        Assert.Equal("acme.io", CrmNormalization.EmailDomain("Jane@ACME.io"));
        Assert.True(CrmNormalization.IsValidEmail("jane@acme.io"));
        Assert.False(CrmNormalization.IsValidEmail("jane@localhost"));
        Assert.False(CrmNormalization.IsValidEmail("Jane Doe <jane@acme.io>"));
        Assert.Equal(("Ada", "Lovelace King"), CrmNormalization.SplitName(" Ada Lovelace King ", "x"));
        Assert.Equal(new[] { "vip", "seo" }, CrmNormalization.Tags(new[] { " VIP", "seo", "vip", "" }));
    }

    [Fact]
    public void Csv_reader_handles_quotes_newlines_and_bom()
    {
        var rows = CsvReader.Parse("﻿first_name,email,tags\r\n\"Doe, Jane\",jane@acme.io,\"a;\"\"b\"\"\"\r\n\r\nBob,,\n", 10);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Doe, Jane", "jane@acme.io", "a;\"b\"" }, rows[1]);
        Assert.Equal(new[] { "Bob", "", "" }, rows[2]);
        Assert.Equal("crm.import_invalid_csv", Assert.Throws<DomainException>(() => CsvReader.Parse("a,\"b\n", 10)).Code);
        Assert.Equal("crm.import_too_large", Assert.Throws<DomainException>(() => CsvReader.Parse("h\n1\n2\n3\n", 2)).Code);
    }

    [Fact]
    public void Effective_status_expires_unanswered_proposals()
    {
        var today = new DateOnly(2026, 9, 23);
        Assert.Equal(ProposalStatus.Expired, ProposalService.EffectiveStatus(new Proposal { Status = ProposalStatus.Viewed }, today.AddDays(-1), today));
        Assert.Equal(ProposalStatus.Sent, ProposalService.EffectiveStatus(new Proposal { Status = ProposalStatus.Sent }, today, today));
        Assert.Equal(ProposalStatus.Accepted, ProposalService.EffectiveStatus(new Proposal { Status = ProposalStatus.Accepted }, today.AddDays(-9), today));
    }
}
