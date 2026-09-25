# Partner content strategy: PCI AI and Certuvo

Optimize All is the official marketing partner of **PCI AI** (pciai.org) and **Certuvo** (certuvo.com). This document
sets out how our blog and guest-post programme grows search visibility and reach for both partners, while building
Optimize All's own topical authority and keeping every link compliant with search-engine guidelines.

- Blog posts (19): `backend/src/OptimizeAll.Api/Modules/Website/Content/partner-posts/*.md`, seeded as published posts
  by `PartnerContentSeeder` (see `docs/WEBSITE.md` → Seeds).
- Guest posts (9) and outreach process: `docs/marketing/guest-posts/`.

## 1. Facts we may use (and nothing else)

All partner claims in our content come from the partners' own positioning. Do not add features, statistics, prices,
customers or dates that are not listed here, do not name or compare competitors, and do not publish time-limited
discount codes.

**PCI AI** — "The credential for the people who control projects." PCL-AI is "one rigorous standard uniting planning,
scheduling, cost, earned value, forecasting and project finance — with responsible AI built into every part. Open to
anyone with three years' experience, in any field." Fully online, scenario-based exams; AI governed throughout. Three
certifications, each USD 350, 90 minutes, 65% to pass, valid 3 years:

- **PCL-AI** — PCI AI Project Controls Leader (planning, cost engineering, earned value, forecasting, risk and project
  finance with the governed use of AI).
- **PFL-AI** — PCI AI Project Finance Leader (project finance, financial modelling, capital structure, bankability,
  DSCR/LLCR/PLCR, PPP and concession structures, financial close, AI-enabled analysis).
- **PML-AI** — PCI Project Management Leader – AI (governance, planning, execution, agile/hybrid delivery, AI-enabled
  project management).

The site also has a certification roadmap, eligibility requirements, exam structure, body of knowledge, sample
questions, recertification, digital credentials and student membership enrolment.

**Certuvo** — exam preparation for **CIA, CISA, CMA, CPA, CFA, PMP, NCLEX-RN, NCLEX-PN** and PCI AI's **PCL-AI, PFL-AI
and PML-AI**. Thousands of exam-style questions written and verified by qualified professionals and mapped to the
official blueprint, expanded with personalised AI practice, every question validated by four independent AI judges.
From $60 for 12 months. AI Coach you can chat with or call during practice; Socratic ("teach you to think, not
memorize"); speaks English, Arabic, French, Spanish, Hindi and Russian; reads your screen (question, diagrams, tables,
answer options); references exam standards (ASC 606, IFRS 15, IPPF 2200, PMBOK 7, COBIT 2019, NGN); automatically
disabled during mock exams. (Prices change: the blog posts deliberately do not quote the price.)

Exam facts about third-party bodies (AICPA/NASBA, IMA, IIA, ISACA, CFA Institute, PMI, NCSBN) are kept at a general level,
and every Certuvo-cluster post carries a "check the official body's current requirements" note.

## 2. Keyword clusters

One primary keyword per post; no two posts target the same query (enforced by `PartnerPostLibraryTests`).

### Cluster A — Project controls and project finance (PCI AI)

| Post (slug) | Primary keyword | Search intent | Partner credential |
|---|---|---|---|
| what-is-project-controls (**pillar**) | what is project controls | Informational | PCL-AI |
| earned-value-management-explained | earned value management | Informational / learning | PCL-AI |
| ai-in-project-controls | AI in project controls | Informational / emerging | PCL-AI |
| how-to-become-a-project-controls-professional | project controls career | Career research | PCL-AI (+PFL/PML) |
| dscr-vs-llcr-vs-plcr | DSCR vs LLCR vs PLCR | Informational / technical | PFL-AI |
| ppp-and-concession-structures-explained | PPP concession structure | Informational | PFL-AI |
| agile-vs-hybrid-project-delivery-with-ai | agile vs hybrid project management | Informational / comparison | PML-AI |
| choosing-a-project-management-certification | project management certification | Commercial investigation | All three (neutral) |

Secondary terms to weave in naturally: project controls vs project management, CPI and SPI, estimate at completion,
TCPI, CFADS, debt sizing, availability payment, SPV, hybrid project management, AI governance in projects, PCL-AI.

### Cluster B — Professional exam preparation (Certuvo)

| Post (slug) | Primary keyword | Search intent |
|---|---|---|
| how-to-pass-the-cpa-exam | how to pass the CPA exam | Informational / planning |
| cma-exam-study-plan | CMA exam study plan | Planning |
| cia-exam-preparation-guide | CIA exam preparation | Planning |
| cisa-exam-preparation-guide | CISA exam preparation | Planning |
| cfa-exam-study-strategies | CFA exam study strategies | Planning |
| pmp-exam-prep-pmbok-7 | PMP exam prep | Planning |
| nclex-ngn-question-types | NGN question types | Informational |
| nclex-rn-vs-nclex-pn | NCLEX-RN vs NCLEX-PN | Comparison |
| how-to-use-an-ai-study-coach | AI study coach | Informational / emerging |
| spaced-repetition-and-active-recall-for-exams (**pillar**) | spaced repetition for exams | Informational |

### Cross-partner (bridge)

| Post (slug) | Primary keyword | Role |
|---|---|---|
| how-to-prepare-for-pci-ai-exams | PCL-AI exam preparation | Bridges both clusters: PCI AI facts + Certuvo prep for all three credentials. Targets partner-branded queries (PCL-AI, PFL-AI, PML-AI). |

## 3. Pillar / cluster internal-linking map

```
                         /partners/pci-ai                         /partners/certuvo
                               |                                          |
  Cluster A pillar: what-is-project-controls          Cluster B pillar: spaced-repetition-and-active-recall-for-exams
     |-- earned-value-management-explained                |-- how-to-use-an-ai-study-coach
     |-- ai-in-project-controls                           |-- how-to-pass-the-cpa-exam
     |-- how-to-become-a-project-controls-professional    |-- cma-exam-study-plan
     |-- dscr-vs-llcr-vs-plcr                              |-- cia-exam-preparation-guide <-> cisa-exam-preparation-guide
     |-- ppp-and-concession-structures-explained           |-- cfa-exam-study-strategies
     |-- agile-vs-hybrid-project-delivery-with-ai  <-------+-- pmp-exam-prep-pmbok-7
     '-- choosing-a-project-management-certification      |-- nclex-ngn-question-types <-> nclex-rn-vs-nclex-pn
                     \                                    /
                      '--> how-to-prepare-for-pci-ai-exams <--'   (bridge: links both partner pages)
```

Rules (checked by `PartnerPostLibraryTests`):

- Every post links to at least one Academy course (`/learn/<slug>`) and to its partner page (`/partners/pci-ai` or
  `/partners/certuvo`; the bridge post links both).
- Internal links only point to the ten Academy course slugs, the two partner pages and other partner posts.
- Cluster posts link up to their pillar and sideways to 1–3 siblings; pillars link down to cluster posts. The
  `related` front-matter field sets the "related posts" block (resolved by slug at seeding).
- 1–3 contextual links to the partner site per post (the tests allow at most 3); the site marks partner links
  `rel="sponsored"` automatically.
- Every post ends with the disclosure line: *Optimize All is the official marketing partner of PCI AI and Certuvo.*

Academy course mapping:

| Course | Linked from |
|---|---|
| project-controls-with-ai | Cluster A pillar, EVM, AI in controls, career, PCI AI prep |
| project-finance-and-financial-modelling | DSCR, PPP, career, PCI AI prep |
| project-management-leadership-with-ai | Agile vs hybrid, certification choice, PMP, PCI AI prep |
| professional-certification-exam-success | Every Cluster B post, certification choice, PCI AI prep |
| leadership-and-communication | Career, agile vs hybrid, CIA, PCI AI prep |
| ai-for-data-analysis-and-decision-making | AI in controls, DSCR, CMA, CFA, CISA |
| prompt-engineering-foundations | AI in controls, career, AI study coach, spaced repetition |
| mastering-claude / mastering-chatgpt | AI study coach |
| advanced-prompt-engineering | Reserved for future AI-governance posts |

## 4. Publishing calendar

The seed publishes all 19 posts at once with dates staggered over the previous 30 days (so the blog does not show a
single-day burst). For **new** content and refreshes, publish **2 posts a week** (Tuesday and Thursday), alternating
clusters, and promote each on LinkedIn and the newsletter the same week.

| Week | Tuesday | Thursday |
|---|---|---|
| 1 | Refresh + promote: What is project controls (pillar A) | Refresh + promote: Spaced repetition (pillar B) |
| 2 | Promote: EVM explained | Promote: How to pass the CPA exam |
| 3 | Promote: DSCR vs LLCR vs PLCR | Promote: NGN question types |
| 4 | Promote: PCI AI exam prep (bridge) | Promote: AI study coach |
| 5–8 | New: earned schedule; risk-adjusted forecasting; lender's technical adviser role; IFRS 15 vs ASC 606 study notes | New: CISA domain 5 deep-dive; NCLEX prioritisation frameworks; CFA Level II item sets; PMP agile scenarios |
| 9–12 | Guest posts go live (see guest-posts/README.md); refresh any post not in top 20 for its keyword after 8 weeks | |

Refresh cadence: review every post quarterly; update exam facts whenever an official body changes its outline.

## 5. Measuring success

| Goal | Metric | Source | Target (first 6 months) |
|---|---|---|---|
| Search visibility for our pages | Impressions, clicks, average position for each primary keyword; number of queries per page | Google Search Console (filter by page `/blog/<slug>`) | Every post indexed; 50% of posts in top 20 for their primary keyword |
| Topical authority | Non-branded queries per cluster; pages ranking for secondary terms | Search Console (regex query filter per cluster) | Growth month on month |
| Referral traffic to partners | Clicks on partner links | UTM-tagged links (`utm_source=optimizeall&utm_medium=referral&utm_campaign=blog-<slug>`) reported by the partners; our own outbound-click events in GA4 | Tracked for every post |
| Partner-branded search | Search interest for "PCI AI", "PCL-AI", "PFL-AI", "PML-AI", "Certuvo" | Google Trends; partners' Search Console branded queries | Upward trend vs pre-launch baseline |
| Brand mentions | Unlinked and linked mentions of Optimize All, PCI AI, Certuvo on third-party sites | Guest-post tracking sheet; mention alerts | 9 guest posts live |
| Engagement | Scroll depth, time on page, clicks to `/learn/*` and `/partners/*` | GA4 | Academy click-through from every post |
| Academy growth | Enrolments in linked courses attributed to blog referrers | Academy analytics | Tracked monthly |

UTM note: add UTM parameters to partner links **in newsletter and social promotion**, and ask partners to report
referral sessions from `optimizeall` sources; keep blog body links clean and let GA4 outbound-click events plus partner
referral reports measure them. Review all metrics monthly in a one-page report.

## 6. Link policy and rationale

**Why partner links are `rel="sponsored"`.** Optimize All has a commercial relationship with PCI AI and Certuvo. Google's
guidance asks that links that are part of advertising, sponsorships or other compensation arrangements be qualified
with `rel="sponsored"` (or `nofollow`). The site marks partner links as sponsored automatically, and every post
discloses the relationship in plain words. The same applies to guest posts: we ask publishers to mark the partner link
`rel="sponsored"` (or `nofollow`) and to keep our disclosure in the author bio.

**Where the value comes from instead.** We are not trying to pass PageRank. The programme creates value through:

1. **Reach** — genuinely useful posts that rank for high-intent queries (exam preparation, project controls careers,
   project finance ratios) and put both partners in front of the right audience.
2. **Brand mentions** — consistent, factual mentions of PCI AI, PCL-AI/PFL-AI/PML-AI and Certuvo alongside expert
   content, which builds recognition and branded search.
3. **Topical authority** — two tightly interlinked clusters that establish Optimize All as a credible source on project
   controls and certification preparation, lifting all posts and the Academy.
4. **Referral traffic** — readers who click through because the link is relevant at the moment they need it.

**Guardrails.** No paid "follow" links, no link exchanges, no exact-match anchor stuffing, no more than three partner
links per post (one in guest posts), no invented facts, no competitor comparisons, no time-limited discount codes.
