---
slug: earned-value-management-explained
title: Earned Value Management Explained: A Worked Example
description: Earned value management step by step: PV, EV, AC, CPI, SPI, EAC and TCPI with a full worked example, common traps and how to read the numbers.
cluster: pci-ai
primaryKeyword: earned value management
categories: project-controls
tags: earned value, project controls, forecasting
related: what-is-project-controls, ai-in-project-controls, how-to-prepare-for-pci-ai-exams
publishedDaysAgo: 26
cover: project-controls
coverAlt: Dark blue cover with horizontal schedule bars and the words Project controls and finance
---
Earned value management (EVM) answers a question that budgets and schedules cannot answer on their own: **for the money we have spent, how much of the planned work have we actually achieved?** A project can be on budget and still in trouble — if it has spent exactly what it planned but delivered only half the work. EVM makes that visible early, while there is still time to act.

This guide builds EVM from first principles, runs a complete worked example, and ends with the traps that make earned value numbers lie.

## The three numbers everything is built on

EVM compares three values, all expressed in currency.

- **Planned value (PV)** — the budgeted cost of the work *scheduled* to be done by a given date. Some texts call it BCWS.
- **Earned value (EV)** — the budgeted cost of the work *actually completed* by that date. Also called BCWP.
- **Actual cost (AC)** — what the completed work *actually cost*. Also called ACWP.

Two more anchor the forecast:

- **Budget at completion (BAC)** — the total approved budget for the work.
- **Estimate at completion (EAC)** — the expected total cost when the work is finished.

The key insight is that EV is measured in *budget* terms. If a work package was budgeted at 100,000 and is 40% complete, it has earned 40,000 — regardless of what was actually spent.

## Variances and indices

From the three core values come two variances and two indices.

| Measure | Formula | Reading |
|---|---|---|
| Cost variance (CV) | EV − AC | Negative = over cost |
| Schedule variance (SV) | EV − PV | Negative = behind schedule |
| Cost performance index (CPI) | EV ÷ AC | Below 1.0 = each unit of spend earns less than planned |
| Schedule performance index (SPI) | EV ÷ PV | Below 1.0 = progressing slower than planned |

And three forecasting measures:

| Measure | Formula | Use |
|---|---|---|
| EAC (CPI method) | BAC ÷ CPI | Assumes current cost efficiency continues |
| EAC (CPI × SPI method) | AC + (BAC − EV) ÷ (CPI × SPI) | Assumes schedule pressure will also affect cost |
| To-complete performance index (TCPI) | (BAC − EV) ÷ (BAC − AC) | The efficiency needed on remaining work to finish on budget |

## A worked example: a telemetry installation programme

A utility is installing telemetry units at 40 pumping stations. Each station is a work package with a budget of 50,000, so **BAC = 2,000,000**. The plan is to complete four stations per month over ten months.

Rules of credit for each station (agreed before work starts):

- Survey complete: 10%
- Equipment delivered to site: 30%
- Installed: 40%
- Commissioned and accepted: 20%

### Status at the end of month 5

The schedule says 20 stations should be complete by now, so **PV = 20 × 50,000 = 1,000,000**.

Actual status from the field:

- 14 stations fully commissioned: 14 × 50,000 = 700,000 earned
- 4 stations installed but not commissioned: 4 × 50,000 × 80% = 160,000
- 3 stations with equipment delivered only: 3 × 50,000 × 40% = 60,000
- 2 stations surveyed only: 2 × 50,000 × 10% = 10,000

**EV = 700,000 + 160,000 + 60,000 + 10,000 = 930,000**

Finance reports **AC = 1,050,000** to date (including accruals for work done but not yet invoiced — an important detail).

### The indices

```
CV  = EV - AC = 930,000 - 1,050,000 = -120,000
SV  = EV - PV = 930,000 - 1,000,000 =  -70,000
CPI = EV / AC = 930,000 / 1,050,000 = 0.886
SPI = EV / PV = 930,000 / 1,000,000 = 0.930
```

Reading: every 1.00 spent is earning about 0.89 of planned value, and the programme has achieved 93% of the work it planned by this date.

### The forecasts

```
EAC (CPI)        = 2,000,000 / 0.886                         = 2,257,000 (approx.)
EAC (CPI x SPI)  = 1,050,000 + (2,000,000 - 930,000) / (0.886 x 0.930)
                 = 1,050,000 + 1,070,000 / 0.824             = 2,348,000 (approx.)
TCPI (to BAC)    = (2,000,000 - 930,000) / (2,000,000 - 1,050,000)
                 = 1,070,000 / 950,000                       = 1.126
```

What these say together:

- If cost efficiency stays where it is, the programme finishes around **2.26 million** — roughly 13% over budget.
- If schedule pressure also drives cost (overtime, expediting), it could reach **2.35 million**.
- To finish on the original budget, the remaining work must be performed at a CPI of **1.13** — materially better than anything achieved so far. That is usually a sign the budget target is no longer realistic and a formal re-forecast is needed.

### Finding the cause

EVM tells you *that* something is wrong, not *why*. Breaking the variance down by station quickly shows that the four "installed but not commissioned" stations have consumed extra cost: commissioning is waiting on a software configuration from the vendor, and crews have made repeat visits. The action is not "spend less"; it is "fix the vendor dependency and stop sending crews before configurations are ready."

## The EVM health check: five questions before you trust the numbers

Earned value is only as honest as its inputs. Before presenting CPI or SPI to anyone, ask:

1. **Were rules of credit agreed before work started?** If progress percentages are subjective, EV is opinion in currency form.
2. **Does AC include accruals?** If invoices lag, AC is understated and CPI looks better than reality.
3. **Is the baseline current and approved?** Unapproved scope changes silently corrupt PV.
4. **Is level-of-effort work separated?** Support activities that "earn" value simply by time passing will dilute genuine performance signals.
5. **Does SPI still mean something late in the project?** Standard SPI drifts back towards 1.0 as the project nears completion even if it is late, because eventually all planned value is earned. Many teams supplement it with earned schedule techniques or critical-path analysis in the final third of a project.

## How earned value fits into the wider control cycle

EVM is the measurement and analysis engine of project controls, but it is not the whole car. It needs:

- a sound, logic-linked **schedule** to generate meaningful PV;
- a **cost breakdown structure** aligned to the work breakdown so EV and AC are compared like for like;
- **risk analysis** so forecasts come with ranges rather than a single point;
- **governance**, so the story told by the numbers turns into decisions.

For the bigger picture, see [What is project controls?](/blog/what-is-project-controls).

## Where AI helps — and where it should not

AI tools are genuinely useful in EVM work: reconciling cost lines to control accounts, flagging stations whose reported progress conflicts with photos or delivery records, drafting variance narratives, and generating forecast ranges from historical performance. The risk is automation of the wrong thing — for example, letting a model infer progress percentages that should come from agreed rules of credit. Treat AI as an analyst whose work you review, and document which outputs were AI-assisted. Our guide to [AI in project controls](/blog/ai-in-project-controls) goes further.

## Learn it properly

The fastest way to internalise EVM is to calculate it by hand on a small project, then again on a spreadsheet, then again with messy real data. Optimize All's free course [Project Controls with AI](/learn/project-controls-with-ai) includes exercises like the one above.

Earned value, forecasting and the governed use of AI are all part of the body of knowledge for the [PCI AI](https://pciai.org) PCL-AI (PCI AI Project Controls Leader) certification, whose 90-minute, scenario-based online exam requires 65% to pass. If that credential is on your roadmap, our post on [preparing for the PCL-AI, PFL-AI and PML-AI exams](/blog/how-to-prepare-for-pci-ai-exams) sets out a study plan, and our [PCI AI partner page](/partners/pci-ai) summarises the certifications.

## Frequently asked questions

### What is a good CPI?

A CPI of 1.0 means you are earning exactly the planned value for what you spend. Values slightly above 1.0 are healthy; persistent values below about 0.95 usually warrant investigation. What matters most is the trend and the cause, not a single month's figure.

### Which EAC formula should I use?

There is no single right answer. The CPI method suits situations where past cost efficiency is expected to continue. The CPI × SPI method suits projects under schedule pressure. When the original assumptions are no longer valid, a bottom-up re-estimate of remaining work is more reliable than any formula. Many teams present two or three methods as a range.

### Can earned value work on agile projects?

Yes, with adaptation. Teams commonly treat a release or a set of prioritised features as the baseline and measure earned value by completed, accepted items. The principles — plan, measure achievement, compare to cost — still hold.

### What is the difference between percent spent and percent complete?

Percent spent is AC ÷ BAC; percent complete is EV ÷ BAC. Confusing the two is the single most common earned value mistake, and it hides overruns until late in the project.

---

*Optimize All is the official marketing partner of PCI AI and Certuvo.*
