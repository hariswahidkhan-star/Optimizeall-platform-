---
slug: dscr-vs-llcr-vs-plcr
title: DSCR vs LLCR vs PLCR: Project Finance Ratios Explained
description: DSCR, LLCR and PLCR explained with one worked example: what each ratio measures, how lenders use them and the modelling mistakes to avoid.
cluster: pci-ai
primaryKeyword: DSCR vs LLCR vs PLCR
categories: project-controls
tags: project finance, financial modelling, dscr
related: ppp-and-concession-structures-explained, what-is-project-controls, how-to-prepare-for-pci-ai-exams
publishedDaysAgo: 17
cover: project-controls
coverAlt: Dark blue cover with horizontal schedule bars and the words Project finance and project controls
---
In project finance, lenders are repaid from the cash a single project generates — not from a corporate balance sheet. That makes one question central to every deal: **is there enough cash, with enough margin, to pay the debt?** Three coverage ratios answer it from different angles: the debt service coverage ratio (DSCR), the loan life coverage ratio (LLCR) and the project life coverage ratio (PLCR).

This guide explains each ratio, calculates all three on one simple project, and highlights where models commonly go wrong.

## The building block: cash flow available for debt service

All three ratios start from **cash flow available for debt service (CFADS)**. In simplified form:

```
CFADS = Revenue
      - Operating costs
      - Tax paid
      - Changes in working capital
      - Maintenance capital expenditure (if not funded from reserves)
```

CFADS is the cash left over to pay lenders before anything goes to equity holders. Getting CFADS right matters more than any ratio formula — every ratio inherits its errors.

## DSCR: the period-by-period test

**DSCR = CFADS for the period ÷ debt service for the period**

Debt service is scheduled principal plus interest. A DSCR of 1.30 means the project generates 1.30 units of cash for every unit of debt service due in that period.

Lenders use DSCR in three ways:

- **Sizing** — the maximum debt is often set so that forecast DSCR never falls below a target in any period (sculpting repayments to cash flow achieves this efficiently).
- **Lock-up** — if actual DSCR falls below a threshold, distributions to equity may be blocked.
- **Default** — a lower threshold may trigger an event of default.

The exact thresholds depend on the sector, the revenue risk and the lenders; there is no universal number. Contracted, availability-based revenues typically support lower minimum ratios than merchant or demand-risk revenues.

## LLCR: the whole-loan test

**LLCR = NPV of CFADS over the remaining loan life ÷ debt outstanding**

The net present value (NPV) is calculated using the debt's interest rate as the discount rate. LLCR asks: if we add up (in today's money) all the cash available to lenders until the loan's final maturity, how many times does it cover what is still owed?

LLCR smooths out single bad periods. A project might have one weak year with a DSCR of 1.10 but an LLCR of 1.40 because the rest of the loan life is strong.

## PLCR: the whole-project test

**PLCR = NPV of CFADS over the remaining project life ÷ debt outstanding**

PLCR extends the horizon past the loan's maturity to the end of the project's life (for example the end of a concession). The extra years after the debt is repaid are the **tail**. Lenders like a healthy tail because it gives room to restructure if something goes wrong: debt could be extended into those years.

By construction, PLCR is greater than or equal to LLCR when post-maturity cash flows are positive.

## One worked example, three ratios

A small solar project has a six-year remaining life. The loan has four years remaining, with 30.0 million outstanding and an interest rate of 6%. Forecast CFADS and scheduled debt service (in millions):

(Debt service is principal plus 6% interest on the opening balance, and it repays the 30.0 million exactly by the end of year 4.)

| Year | CFADS | Debt service | DSCR |
|---|---|---|---|
| 1 | 11.0 | 8.5 | 1.29 |
| 2 | 11.5 | 8.7 | 1.32 |
| 3 | 10.0 | 8.9 | 1.12 |
| 4 | 11.8 | 8.54 | 1.38 |
| 5 | 12.0 | — | — |
| 6 | 12.0 | — | — |

**DSCR** is shown per year. The minimum is **1.12 in year 3** — probably a year with planned inverter replacement reducing CFADS. If the loan agreement has a lock-up at, say, 1.15, equity distributions would be blocked that year even though the project is fundamentally sound.

**LLCR** discounts years 1–4 at 6%:

```
NPV = 11.0/1.06 + 11.5/1.06^2 + 10.0/1.06^3 + 11.8/1.06^4
    = 10.38 + 10.23 + 8.40 + 9.35
    = 38.36
LLCR = 38.36 / 30.0 = 1.28
```

**PLCR** adds years 5 and 6:

```
NPV (years 5-6) = 12.0/1.06^5 + 12.0/1.06^6 = 8.97 + 8.46 = 17.43
NPV (years 1-6) = 38.36 + 17.43 = 55.79
PLCR = 55.79 / 30.0 = 1.86
```

### What the three numbers tell a lender

- DSCR flags a **timing** problem in year 3 — perhaps solvable with a maintenance reserve account funded in years 1–2.
- LLCR of 1.28 says the loan is **comfortably covered** over its life.
- PLCR of 1.86 says there is a **substantial tail** — room to reschedule debt if production underperforms.

Read together, they turn "the ratio is low in year 3" from a red flag into a design question.

## The COVER checklist for coverage-ratio models

Before trusting any ratio output, check:

- **C — CFADS definition** matches the loan documents exactly (what is deducted, what is funded from reserves).
- **O — Only scheduled debt service** is in the DSCR denominator, consistently including or excluding fees as the documents require.
- **V — Valuation rate** for LLCR and PLCR is the rate specified in the documents (often the all-in debt rate), applied to the correct period lengths.
- **E — Ending points** are right: loan maturity for LLCR, end of project or concession life for PLCR.
- **R — Reserves** are treated consistently: cash in a debt service reserve account is sometimes added to the numerator of LLCR, sometimes not.

## Common mistakes

- **Using annual ratios for semi-annual debt.** Ratios should follow the repayment period.
- **Averaging DSCRs to "summarise" the loan.** An average hides the minimum, which is what covenants test.
- **Ignoring tax timing.** Paying tax a year in arrears changes CFADS in specific periods.
- **Circularity shortcuts.** Sculpted debt, interest during construction and fees create circular references; poorly handled, they produce ratios that are subtly wrong.

## How this connects to project controls

Coverage ratios are only as good as the cash-flow forecast, and that forecast depends on construction completing on time and on budget. A three-month delay in commercial operation pushes revenue back while interest keeps accruing — squeezing early DSCRs and sometimes breaching covenants before the project has earned a single unit of revenue. That is why project controls and project finance are increasingly treated as one skill set. See [What is project controls?](/blog/what-is-project-controls) and, for the contractual side, [PPP and concession structures explained](/blog/ppp-and-concession-structures-explained).

## Going further

Optimize All's free course [Project Finance and Financial Modelling](/learn/project-finance-and-financial-modelling) builds a coverage-ratio model step by step. For AI-assisted modelling and analysis, [AI for Data Analysis and Decision-Making](/learn/ai-for-data-analysis-and-decision-making) is a helpful companion.

If you want a credential in this area, the [PCI AI](https://pciai.org) **PFL-AI — PCI AI Project Finance Leader** covers project finance, financial modelling, capital structure, bankability, coverage ratios (DSCR, LLCR and PLCR), PPP and concession structures, financial close and AI-enabled analysis. Like PCI AI's other certifications, it has a 90-minute exam with a 65% pass mark, a USD 350 exam fee and three-year validity. See our [PCI AI partner page](/partners/pci-ai) for an overview.

## Frequently asked questions

### What is the difference between DSCR and LLCR?

DSCR tests one period at a time: cash available that period divided by debt service due that period. LLCR tests the whole remaining loan: the present value of cash available until maturity divided by the debt outstanding. DSCR catches timing problems; LLCR shows overall debt capacity.

### Why is PLCR higher than LLCR?

PLCR includes cash flows after the loan matures (the tail). If those cash flows are positive, the numerator is larger while the debt outstanding is the same, so PLCR exceeds LLCR.

### What is a good DSCR?

It depends on revenue risk, sector and lender requirements. Projects with contracted revenues generally need lower minimum DSCRs than those exposed to market prices or demand. Always refer to the specific term sheet.

### Is DSCR used outside project finance?

Yes. Commercial real estate lending and corporate lending use DSCR too, though definitions of the cash-flow numerator vary. In project finance, the numerator is specifically CFADS.

---

*Optimize All is the official marketing partner of PCI AI and Certuvo.*
