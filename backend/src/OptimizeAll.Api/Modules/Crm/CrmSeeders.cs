using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Crm;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Crm;

/// <summary>Baseline: the default sales pipeline and lead-scoring rules (only when none exist; edit them in CRM settings).</summary>
public sealed class CrmBaselineSeeder : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 45;

    public static readonly (string Name, int Probability, StageKind Kind)[] DefaultStages =
    {
        ("New", 5, StageKind.Open),
        ("Contacted", 10, StageKind.Open),
        ("Qualified", 25, StageKind.Open),
        ("Discovery call", 40, StageKind.Open),
        ("Proposal sent", 60, StageKind.Open),
        ("Negotiation", 80, StageKind.Open),
        ("Won", 100, StageKind.Won),
        ("Lost", 0, StageKind.Lost),
    };

    public static readonly (string Name, ScoringCategory Category, string Field, string? Match, int Points, int? Max)[] DefaultRules =
    {
        ("Target industry", ScoringCategory.Fit, "industry", "saas,e-commerce,ecommerce,travel,healthcare,fintech,SaaS fitness app,E-commerce beauty", 15, null),
        ("Mid-size or larger company", ScoringCategory.Fit, "companySize", "Medium,Large,Enterprise", 15, null),
        ("Small company", ScoringCategory.Fit, "companySize", "Small", 8, null),
        ("Budget 5k+ per month", ScoringCategory.Fit, "budgetRange", "5k-10k,10k-25k,25k+", 20, null),
        ("Budget 2k–5k per month", ScoringCategory.Fit, "budgetRange", "2k-5k", 10, null),
        ("Core market", ScoringCategory.Fit, "country", "US,GB,AE,SA,PK", 5, null),
        ("Referral", ScoringCategory.Fit, "source", "Referral", 10, null),
        ("Website inquiry", ScoringCategory.Engagement, "website_inquiry", null, 20, 3),
        ("Form submitted", ScoringCategory.Engagement, "form_submitted", null, 10, 5),
        ("Email clicked", ScoringCategory.Engagement, "email_clicked", null, 3, 10),
        ("Email opened", ScoringCategory.Engagement, "email_opened", null, 1, 10),
        ("Proposal viewed", ScoringCategory.Engagement, "proposal_viewed", null, 5, 5),
        ("Meeting booked", ScoringCategory.Engagement, "meeting_booked", null, 25, 2),
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.Set<PipelineStage>().AnyAsync(ct))
        {
            var position = 10;
            foreach (var (name, probability, kind) in DefaultStages)
            {
                db.Set<PipelineStage>().Add(new PipelineStage
                {
                    Name = name, WinProbability = probability, Kind = kind,
                    Position = kind switch { StageKind.Won => 1000, StageKind.Lost => 1001, _ => position },
                });
                position += 10;
            }
        }
        if (!await db.Set<LeadScoringRule>().AnyAsync(ct))
            foreach (var (name, category, field, match, points, max) in DefaultRules)
                db.Set<LeadScoringRule>().Add(new LeadScoringRule
                {
                    Name = name, Category = category, Field = field, MatchValue = match, Points = points, MaxOccurrences = max,
                });
        await db.SaveChangesAsync(ct);
    }
}
