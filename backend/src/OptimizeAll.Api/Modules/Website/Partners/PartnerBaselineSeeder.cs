using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>
/// The partners Optimize All is the official marketing partner of: PCI AI and Certuvo. Runs in every environment
/// (Baseline), creates each partner once (keyed by slug in the seed ledger), and never overwrites edits made in
/// Website → Partners. The texts only state facts taken from the partners' own websites (pciai.org and certuvo.com, as
/// supplied on 2026-09-25; see docs/WEBSITE.md) — have the partners confirm them, and keep them current in the admin.
/// </summary>
public sealed class PartnerBaselineSeeder(TimeProvider clock) : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 61;

    public const string PciAiSlug = "pci-ai";
    public const string CertuvoSlug = "certuvo";

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var ledger = await SeedLedger.LoadAsync(db, "website_partners", ct);
        var existing = await db.Set<WebsitePartner>().ToDictionaryAsync(p => p.Slug, ct);
        var created = new List<WebsitePartner>();
        foreach (var seed in new[] { PciAi(), Certuvo(clock.GetUtcNow().UtcDateTime) })
        {
            var key = "partner:" + seed.Slug;
            var seeded = ledger.WasSeeded(key);
            ledger.Record(key);
            if (seeded || existing.ContainsKey(seed.Slug)) continue;
            db.Add(seed);
            created.Add(seed);
            existing[seed.Slug] = seed;
        }
        // The two profiles link to each other (Certuvo prepares candidates for PCI AI's certifications).
        if (created.Count > 0 && existing.TryGetValue(PciAiSlug, out var pci) && existing.TryGetValue(CertuvoSlug, out var certuvo))
        {
            if (created.Contains(pci) && !pci.RelatedPartnerIds.Contains(certuvo.Id)) pci.RelatedPartnerIds.Add(certuvo.Id);
            if (created.Contains(certuvo) && !certuvo.RelatedPartnerIds.Contains(pci.Id)) certuvo.RelatedPartnerIds.Add(pci.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    private static readonly string[] Everywhere =
    {
        PartnerSlots.HomeStrip, PartnerSlots.Footer, PartnerSlots.BlogInline, PartnerSlots.BlogEnd, PartnerSlots.ServiceDetail,
        PartnerSlots.CaseStudyDetail,
    };

    public static WebsitePartner PciAi() => new()
    {
        Slug = PciAiSlug,
        Name = "PCI AI",
        LogoUrl = "/partners/pci-ai.png",
        WebsiteUrl = "https://pciai.org",
        Tagline = "The credential for the people who control projects",
        RelationshipLabel = PartnerRules.DefaultRelationship,
        DescriptionMarkdown = """
            PCI AI certifies professionals in project controls, cost, finance and project management — with the governed use of AI throughout.

            Its flagship credential, **PCL-AI**, is one rigorous standard uniting planning, scheduling, cost, earned value, forecasting and project finance — with responsible AI built into every part. It is open to anyone with three years' experience, in any field.

            Alongside PCL-AI, PCI AI offers the **PFL-AI** (Project Finance Leader) and **PML-AI** (Project Management Leader – AI) credentials. Every exam is taken fully online and is scenario-based.

            PCI AI's website also covers its certification roadmap, eligibility requirements, exam structure, body of knowledge, sample questions, certification policies, recertification and digital credentials, and student membership enrolment.

            Exam preparation for all three PCI AI certifications is available from [Certuvo](/partners/certuvo).

            PCI AI is an independent organization and a separate platform from Optimize All. Optimize All is its official marketing partner.
            """,
        Highlights = new() { "3 years' experience — any field", "Fully online · scenario-based exam", "AI governed throughout" },
        Offerings = new()
        {
            new("PCL-AI — PCI AI Project Controls Leader",
                "The integrated project-controls credential: planning, cost engineering, earned value, forecasting, risk and project finance with the governed use of AI.",
                Facts(), "pcl-ai"),
            new("PFL-AI — PCI AI Project Finance Leader",
                "Project finance, financial modelling, capital structure, bankability, coverage ratios (DSCR/LLCR/PLCR), PPP and concession structures, financial close and AI-enabled analysis.",
                Facts(), "pfl-ai"),
            new("PML-AI — PCI Project Management Leader – AI",
                "Comprehensive project management, leadership and delivery credential covering governance, planning, execution, agile/hybrid delivery and AI-enabled project management.",
                Facts(), "pml-ai"),
        },
        Keywords = new()
        {
            "PCI AI", "PCL-AI", "PCI AI Project Controls Leader", "Project Controls Leader", "PFL-AI", "Project Finance Leader", "PML-AI",
            "Project Management Leader", "project controls certification", "AI project controls", "project finance certification",
            "project delivery certification", "AI project management certification", "AI in project management", "AI forecasting and risk",
            "earned value", "cost engineering", "planning and scheduling", "DSCR LLCR PLCR", "PPP", "agile hybrid delivery", "project controls",
            "project finance", "project management", "AI",
        },
        Categories = new() { "ai", "project-controls", "project-finance", "project-management", "leadership", "finance" },
        Slots = Everywhere.Concat(new[] { PartnerSlots.Careers, PartnerSlots.LearnCourse, PartnerSlots.LearnLesson }).ToList(),
        BrandColor = "#14285A",
        UtmSource = "optimizeall",
        UtmMedium = "partner",
        Seo = new SeoMeta
        {
            Title = "PCI AI — PCL-AI, PFL-AI and PML-AI certifications",
            Description = "PCI AI certifications for project controls (PCL-AI), project finance (PFL-AI) and project management (PML-AI), with responsible AI built in. Fully online exams.",
        },
        IsActive = true,
        SortOrder = 10,
    };

    private static List<string> Facts() => new() { "USD 350 exam fee", "90 minutes", "65% to pass", "Valid for 3 years" };

    public static WebsitePartner Certuvo(DateTime now) => new()
    {
        Slug = CertuvoSlug,
        Name = "Certuvo",
        LogoUrl = "/partners/certuvo.jpg",
        WebsiteUrl = "https://certuvo.com",
        Tagline = "Pass your next exam with total confidence",
        RelationshipLabel = PartnerRules.DefaultRelationship,
        DescriptionMarkdown = """
            Certuvo is an exam-preparation platform. It offers exam preparation for CIA, CISA, CMA, CPA, CFA, PMP, NCLEX-RN and NCLEX-PN, and for PCI AI's [PCL-AI, PFL-AI and PML-AI certifications](/partners/pci-ai).

            Its question bank holds thousands of exam-style questions, written and verified by qualified professionals and mapped to the official blueprint — then expanded with personalized AI practice, with every question validated by four independent AI judges.

            Stuck on a question? Learners can chat with or call Certuvo's **AI Coach** without leaving the practice screen. The coach uses the Socratic method to teach you to think, not memorize, and speaks six languages: English, Arabic, French, Spanish, Hindi and Russian. It is switched off during mock exams — practice with help, test without it.

            Certuvo is an independent platform, separate from Optimize All. Optimize All is its official marketing partner.
            """,
        Highlights = new()
        {
            "Thousands of exam-style questions mapped to the official blueprint", "Every question validated by four independent AI judges",
            "AI Coach by chat or voice call, in six languages", "From $60 for 12 months",
        },
        Offerings = new()
        {
            new("AI Coach", "Chat or call your AI Coach during practice.", new()
            {
                "Voice calls during practice",
                "Reads your screen automatically — the exact question, including diagrams, tables and answer options",
                "References real exam standards: ASC 606, IFRS 15, IPPF 2200, PMBOK 7, COBIT 2019, NGN",
                "Automatically disabled during mock exams",
            }, "ai-coach"),
            new("CIA", null, new(), "cia"),
            new("CISA", null, new(), "cisa"),
            new("CMA", null, new(), "cma"),
            new("CPA", null, new(), "cpa"),
            new("CFA", null, new(), "cfa"),
            new("PMP", null, new(), "pmp"),
            new("NCLEX-RN", null, new(), "nclex-rn"),
            new("NCLEX-PN", null, new(), "nclex-pn"),
            new("PCL-AI", "PCI AI Project Controls Leader", new(), "pcl-ai", "/partners/pci-ai#pcl-ai"),
            new("PFL-AI", "PCI AI Project Finance Leader", new(), "pfl-ai", "/partners/pci-ai#pfl-ai"),
            new("PML-AI", "PCI Project Management Leader – AI", new(), "pml-ai", "/partners/pci-ai#pml-ai"),
        },
        Keywords = new()
        {
            "Certuvo", "CPA exam prep", "CMA exam prep", "CIA exam prep", "CFA exam prep", "PMP exam prep", "CISA exam prep", "NCLEX prep",
            "NCLEX-RN prep", "NCLEX-PN prep", "AI exam coach", "practice questions", "mock exams", "certification exam preparation",
            "professional certification", "PCL-AI exam prep", "PFL-AI exam prep", "PML-AI exam prep", "IT audit certification prep",
            "exam preparation", "certification", "certificates", "credentials", "accounting", "nursing", "IT audit",
            "cybersecurity governance", "project management certification",
        },
        Categories = new()
        {
            "certification", "certifications", "exam-prep", "careers", "accounting", "finance", "business", "data", "project-management",
            "it-audit", "cybersecurity", "nursing",
        },
        Slots = Everywhere.Concat(new[]
        {
            PartnerSlots.LearnCourse, PartnerSlots.LearnLesson, PartnerSlots.LearnExam, PartnerSlots.LearnCertificate, PartnerSlots.LearnDashboard,
        }).ToList(),
        BrandColor = "#1D4ED8",
        UtmSource = "optimizeall",
        UtmMedium = "partner",
        // Shown on certuvo.com on 2026-09-25 without an end date: kept unconfirmed until an editor checks it is still running.
        OfferText = "Launch offer: 50% off your first year",
        OfferCode = "LAUNCH50",
        OfferConfirmed = false,
        OfferUpdatedAt = now,
        Seo = new SeoMeta
        {
            Title = "Certuvo — exam prep for CPA, CMA, CIA, CFA, PMP and NCLEX",
            Description = "Certuvo offers exam preparation for CIA, CISA, CMA, CPA, CFA, PMP, NCLEX-RN, NCLEX-PN and PCI AI certifications, with an AI Coach.",
        },
        IsActive = true,
        SortOrder = 20,
    };
}
