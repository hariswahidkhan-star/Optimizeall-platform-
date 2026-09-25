# Coverage Ratios for Non-Bankers: What DSCR, LLCR and PLCR Tell Project Teams

| Field | Value |
|---|---|
| Target publication type | Finance and accounting publications (corporate finance blogs, infrastructure-finance newsletters, CFO/FP&A communities, energy and infrastructure trade media) |
| Partner | PCI AI |
| Article length | ~1,100 words |
| Suggested anchor text | "project finance certification" (alternative: "PCI AI's PFL-AI credential") |
| Link target | https://pciai.org |
| Link attributes | `rel="sponsored"` (or `rel="nofollow"`) — required |

## Pitch kit

**Three pitch angles**

1. *Why a three-month delay is a banking problem:* How construction slippage flows straight into debt-service coverage.
2. *Coverage ratios for the people who build things:* A plain-English explainer for engineers, PMs and controls teams on financed projects.
3. *One cash flow, three ratios:* A worked example showing why lenders look at DSCR, LLCR and PLCR together.

**Outreach email template**

> Subject: Contributor pitch — coverage ratios explained for project teams
>
> Hi {first name},
>
> {Publication} explains finance topics for practitioners better than most — your recent piece on {topic} is a good
> example. One gap I often see: engineers and project managers on financed projects are affected by DSCR covenants every
> month, yet rarely get a clear explanation of what the ratios measure.
>
> I'd like to offer a ~1,100-word explainer with one worked example covering DSCR, LLCR and PLCR, and a section on how
> construction delays hit coverage. It's vendor-neutral and educational; one optional reference link (marked
> rel="sponsored" — we're the marketing partner of the organisation it points to).
>
> Interested? I can share an outline or full draft.
>
> Best regards,
> {name}, Optimize All Editorial

**Compliance note.** Optimize All is the official marketing partner of PCI AI; the partner link must carry
`rel="sponsored"` (or `nofollow`), disclosed in the bio.

---

## Article

On a project-financed asset — a wind farm, a toll road, a hospital built under a public-private partnership — lenders
are repaid from the cash that single project generates. That is why three ratios dominate lender reporting: the debt
service coverage ratio (DSCR), the loan life coverage ratio (LLCR) and the project life coverage ratio (PLCR).

Engineers, project managers and controls teams on these projects are affected by the ratios every month, often without
anyone explaining them. This article does.

## Start with CFADS

All three ratios use **cash flow available for debt service (CFADS)**: roughly, revenue minus operating costs, tax and
working-capital movements (and some maintenance spending). It is the cash available to pay lenders before anything
reaches the owners.

If CFADS is wrong, every ratio is wrong. That is the first lesson for project teams: the operating cost and maintenance
forecasts you provide feed directly into lender calculations.

## DSCR: can this period's cash pay this period's debt?

**DSCR = CFADS in a period ÷ debt service due in that period** (principal plus interest).

A DSCR of 1.30 means the project generates 1.30 units of cash for each unit of debt service. Lenders set minimum levels
in the loan documents. Fall below one threshold and cash distributions to owners may be locked up; fall below a lower
one and the lenders may have default rights. The thresholds depend on the sector and on how risky the revenues are.

## LLCR: can the rest of the loan be covered?

**LLCR = present value of CFADS until the loan matures ÷ debt outstanding.**

The present value uses the loan's interest rate. LLCR looks through a single weak period to the loan as a whole. A
project can have one poor year and still show a healthy LLCR.

## PLCR: how much cushion does the whole project provide?

**PLCR = present value of CFADS until the end of the project's life ÷ debt outstanding.**

The years after the loan is repaid — the "tail" — are included. A healthy tail gives lenders room to restructure if
things go wrong, because repayment could be extended into those years.

## One example, three views

A solar project has four years left on its loan (30 million outstanding, 6% interest) and six years of remaining life.
CFADS is forecast at 11.0, 11.5, 10.0 and 11.8 million in years 1–4, then 12.0 million in each of years 5 and 6. Debt
service is 8.5, 8.7, 8.9 and 8.54 million.

- **DSCR** by year: 1.29, 1.32, **1.12**, 1.38. Year 3 is weak — perhaps a planned equipment replacement.
- **LLCR:** discounting years 1–4 at 6% gives about 38.4 million; ÷ 30 = **1.28**.
- **PLCR:** adding years 5–6 (about 17.4 million) gives 55.8 million; ÷ 30 = **1.86**.

Read together: the loan is comfortably covered overall and there is a large tail, but year 3 may trigger a
distribution lock-up. The fix might be as simple as reserving cash in years 1–2 for the year-3 replacement. The ratios
turned a vague worry into a specific design question.

## Why project teams should care

**Construction delay hits coverage first.** If commercial operation slips by three months, revenue starts later but
interest keeps accruing. Early DSCRs are squeezed — sometimes enough to breach covenants before the asset has earned
anything. Accurate schedule forecasting is a financial control, not just an operational one.

**Operating cost forecasts are lender inputs.** Underestimating maintenance or overestimating availability inflates
CFADS and flatters every ratio. When actuals arrive, the gap shows up as falling coverage.

**Change requests change the model.** On long-term contracts, a scope change affects capital cost, operating cost and
sometimes revenue. Its effect on coverage should be part of the decision.

## A worked delay scenario

Take the same project during construction. Debt of 30 million is drawn, and interest at 6% (1.8 million a year, or
about 0.15 million a month) is capitalised until commercial operation, when the first debt-service payment falls due
six months later.

Suppose commissioning slips by three months. Three things happen at once:

1. **Interest keeps running** — about 0.45 million of extra interest is added to the loan balance.
2. **Revenue starts later**, so the cash available when the first debt service falls due is lower than planned.
3. **Contingency is consumed** by the extra site costs of the delay, leaving less cushion for anything else.

If the first-year CFADS falls from 11.0 million to, say, 9.2 million because of the late start, while debt service
rises slightly because of the larger balance, the first DSCR could fall from about 1.29 to around 1.06 — below a
typical lock-up level and uncomfortably close to default thresholds. Nothing is wrong with the asset. The schedule
slipped.

This is why lenders employ technical advisers to scrutinise construction progress and why owners on financed projects
invest in strong project controls: the schedule is a financial instrument.

## Common misreadings to avoid

- **Averaging DSCRs.** Covenants test each period; an average hides the minimum.
- **Reading LLCR alone.** A healthy LLCR can coexist with a single-period breach.
- **Ignoring reserve accounts.** Cash held in a debt service reserve account can cover a weak period, and some
  definitions include it in the LLCR calculation — the loan documents decide.

## Three questions for project teams on financed projects

1. What are the DSCR lock-up and default thresholds in our loan documents, and how close is the base case to them?
2. Which of our forecasts (schedule, operating cost, availability, maintenance) feed CFADS, and who checks them?
3. How would a three-month delay or a 10% operating-cost overrun affect the minimum DSCR?

## What to ask your finance colleagues for

Project teams do not need to build the financial model, but they benefit from seeing its outputs. Ask for a one-page
summary each quarter showing forecast minimum DSCR, LLCR and PLCR, the headroom to lock-up and default thresholds, and
the sensitivities that matter most — typically construction delay, operating cost and availability. Seeing that a
two-month slip removes most of the headroom is a powerful motivator for schedule discipline, and it makes the link
between site decisions and lender relationships visible to everyone.

## Bridging two disciplines

Historically, project controls and project finance sat in different departments. On financed projects they cannot: the
schedule and cost forecasts are the foundation of the lenders' model. Professionals who understand both — how a
schedule slip becomes a coverage problem — are increasingly valuable, and some credentials now treat them together; one
example is a [project finance certification](https://pciai.org) that pairs coverage ratios, PPP structures and financial
close with AI-enabled analysis.

The ratios themselves are simple. What matters is knowing which of your numbers they depend on — and making sure those
numbers are honest.

---

## Author bio

*The Optimize All Editorial team writes practical guides on project controls, project finance and professional
certification, and publishes free courses through the Optimize All Academy. Optimize All is the official marketing
partner of PCI AI and Certuvo.*
