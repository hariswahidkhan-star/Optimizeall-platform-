---
slug: ppp-and-concession-structures-explained
title: PPP and Concession Structures Explained
description: How public-private partnerships and concessions work: the SPV, contract chain, payment mechanisms, risk allocation and a worked availability payment.
cluster: pci-ai
primaryKeyword: PPP concession structure
categories: project-controls
tags: project finance, ppp, concessions, risk allocation
related: dscr-vs-llcr-vs-plcr, what-is-project-controls, how-to-prepare-for-pci-ai-exams
publishedDaysAgo: 14
cover: project-controls
coverAlt: Dark blue cover with horizontal schedule bars and the words Project finance and project controls
---
Toll roads, hospitals, schools, airports, water treatment plants and renewable energy projects are often delivered not by a government building and owning the asset directly, but through a **public-private partnership (PPP)** or a **concession**. The structures look intimidating on a diagram — a dozen boxes and arrows — yet they rest on a handful of simple ideas about who pays, who carries which risk and who gets repaid first.

This guide explains those ideas, walks through a typical structure, and runs a worked example of an availability-based payment with deductions.

## PPP and concession: what the words mean

Terminology varies by country, but broadly:

- A **PPP** is a long-term contract in which a private party designs, builds, finances and often operates and maintains a public asset or service, and is paid in a way linked to performance.
- A **concession** is a form of PPP where the private party typically has the right to collect revenue from users — tolls, fares, fees — and bears some or all of the demand risk.

The distinction that matters most in practice is **who pays** and **who carries demand risk**:

| Model | Who pays the private party | Demand risk | Typical assets |
|---|---|---|---|
| User-pays concession | Users (tolls, fares, tariffs) | Mostly private | Toll roads, airports, ports |
| Availability-payment PPP | The public authority, for an available asset | Mostly public | Hospitals, schools, social infrastructure |
| Hybrid | Users plus public top-up or minimum revenue guarantee | Shared | Transit, some toll roads |

## The typical structure

At the centre sits a **special purpose vehicle (SPV)** — a company created solely for the project. Around it:

1. **Grantor or procuring authority** — signs the project agreement or concession agreement with the SPV and sets performance requirements.
2. **Equity sponsors** — own the SPV and invest risk capital, usually a minority share of total funding.
3. **Lenders** — provide the majority of funding as non-recourse or limited-recourse debt, repaid from project cash flows.
4. **Construction contractor** — signs a design-and-build (often fixed-price, date-certain) contract with the SPV.
5. **Operations and maintenance contractor** — runs and maintains the asset under an O&M contract.
6. **Other parties** — insurers, independent engineers, and in some sectors offtakers or suppliers.

The core principle is **back-to-back risk transfer**. The SPV takes risks from the authority under the project agreement and passes them down, as far as possible, to the parties best able to manage them: construction risk to the builder, performance risk to the operator. What cannot be passed down stays with the SPV and its equity.

## Risk allocation: the RAMP rule of thumb

A simple way to test any allocation is **RAMP**: risk should sit with the party best able to

- **R — Reduce** the likelihood of it happening,
- **A — Absorb** the consequences if it does,
- **M — Manage** it day to day,
- **P — Price** it sensibly.

Applied to common risks:

- **Construction cost and delay** — contractor (can manage and price it), backed by liquidated damages and security.
- **Ground conditions** — often shared; transferring unknowable ground risk fully can be expensive.
- **Demand** — private in a user-pays concession; public in an availability model.
- **Change in law** — commonly shared or public for discriminatory changes, since the private party cannot control legislation.
- **Force majeure** — shared, with relief and sometimes termination rights.
- **Inflation** — often partially indexed in the payment mechanism.

Poor allocation shows up as high bid prices, weak competition or, worse, a project that fails later.

## The project lifecycle

1. **Procurement** — the authority runs a competitive process; bidders prepare technical solutions and financial models.
2. **Financial close** — all contracts are signed, conditions precedent satisfied, and funding becomes available. This is the moment the model's assumptions are locked into legal obligations.
3. **Construction** — funded by equity and debt drawdowns; interest during construction is typically capitalised.
4. **Operations** — revenues begin; debt is repaid from CFADS (cash flow available for debt service) over the loan life; equity receives distributions if coverage tests are met.
5. **Handback** — at the end of the term, the asset returns to the authority in a specified condition.

## Worked example: an availability payment with deductions

A hospital PPP pays an annual unitary charge of 24.0 million, paid monthly (2.0 million per month), subject to deductions for unavailability and poor performance.

The payment mechanism says:

- Each functional area has a weighting. Operating theatres are weighted at 10% of the monthly payment.
- If a functional area is unavailable, the deduction is its weighted share of the monthly payment for each day unavailable, divided by the days in the month.
- Performance failures (for example, late cleaning response) earn points; each point deducts 1,000.

In a 30-day month:

- Two operating theatres (together weighted at 10%) are unavailable for 3 days because of a ventilation failure.
- The operator records 45 performance points.

```
Monthly payment               = 2,000,000
Unavailability deduction      = 2,000,000 x 10% x 3 / 30 = 20,000
Performance deduction         = 45 x 1,000              = 45,000
Payment for the month         = 2,000,000 - 20,000 - 45,000 = 1,935,000
```

Who bears the 65,000? Under back-to-back contracts, the SPV passes the deductions to the O&M contractor, usually up to an annual cap. That is why operators track performance points as closely as project controls teams track earned value — deductions are their margin.

For lenders, this matters because deductions reduce CFADS. A model should test whether a plausible level of deductions still keeps coverage ratios above covenant thresholds. See [DSCR vs LLCR vs PLCR](/blog/dscr-vs-llcr-vs-plcr) for how those ratios work.

### What if the same failure happens in a user-pays concession?

In a toll-road concession, there is no availability payment to deduct from. Instead, a lane closure reduces traffic and therefore toll revenue directly, and the concession agreement may add performance penalties for failing to meet service standards. The economic effect on the SPV is similar — lower cash flow — but the risk sits with traffic volumes and user behaviour rather than a contractual formula. That is why demand forecasts are scrutinised so heavily in user-pays deals.

## Where project controls meets PPP

PPPs magnify the cost of poor controls:

- **Construction delay** postpones revenue while interest accrues, and may trigger liquidated damages and lender reporting.
- **Change requests** from the authority need rigorous pricing, because they alter a long-term contract and a financial model.
- **Lifecycle maintenance** must be planned and funded decades ahead; under-forecasting it erodes equity returns late in the concession.
- **Independent engineers** review progress and certify drawdowns, so progress measurement must be evidence-based.

This is why project finance and project controls increasingly sit together. For foundations, see [What is project controls?](/blog/what-is-project-controls).

## Learn more

Optimize All's free course [Project Finance and Financial Modelling](/learn/project-finance-and-financial-modelling) covers PPP structures, payment mechanisms and coverage-ratio modelling in more depth.

For a credential, the [PCI AI](https://pciai.org) PFL-AI (PCI AI Project Finance Leader) certification covers project finance, financial modelling, capital structure, bankability, coverage ratios, PPP and concession structures, financial close and AI-enabled analysis. Our [PCI AI partner page](/partners/pci-ai) summarises the certification, and our guide on [preparing for the PCI AI exams](/blog/how-to-prepare-for-pci-ai-exams) includes a PFL-AI study plan.

## Frequently asked questions

### What is the difference between a PPP and privatisation?

In privatisation, ownership of an asset or company is transferred to the private sector, usually permanently. In a PPP, the private party delivers and operates the asset for a fixed term under a contract, and the asset usually returns to the public sector at the end.

### Why use an SPV?

An SPV ring-fences the project: its assets, contracts and cash flows are separate from the sponsors' other businesses. That allows non-recourse or limited-recourse lending based on the project's own cash flows.

### What is financial close?

Financial close is the point at which all project and finance agreements are signed and conditions precedent are satisfied, so funding can be drawn. Before financial close, the deal can still fail; after it, the parties are committed.

### Who carries demand risk in an availability-payment PPP?

Mostly the public authority. The private party is paid for making the asset available to the required standard, not for how many people use it.

---

*Optimize All is the official marketing partner of PCI AI and Certuvo.*
