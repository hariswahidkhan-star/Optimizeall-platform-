# Five Governance Questions to Ask Before AI Touches Your Project Forecast

| Field | Value |
|---|---|
| Target publication type | Project-management blogs and magazines (PM practitioner communities, PMO leadership newsletters, construction and engineering management sites) |
| Partner | PCI AI |
| Article length | ~1,150 words |
| Suggested anchor text | "a credential that builds governed AI into project controls" (alternative: "PCI AI's PCL-AI certification") |
| Link target | https://pciai.org |
| Link attributes | `rel="sponsored"` (or `rel="nofollow"`) — required; see compliance note |

## Pitch kit

**Three pitch angles**

1. *The governance gap:* AI forecasting tools are spreading faster than PMO policies — here are five questions every PMO should answer first.
2. *Board-ready AI:* How to present an AI-assisted forecast to a steering committee without losing trust.
3. *From black box to audit trail:* A lightweight review process that makes AI outputs defensible in project reporting.

**Outreach email template**

> Subject: Guest article idea — "Five governance questions before AI touches your project forecast"
>
> Hi {first name},
>
> I enjoyed {specific recent article on their site} — especially the point about {detail}. Many of your readers are
> starting to use AI tools for schedules and cost forecasts, but few PMOs have rules for how those outputs are checked
> before they reach a steering committee.
>
> I'd like to offer an original, vendor-neutral article (~1,100 words) with five practical governance questions, a
> worked example of an AI-assisted forecast range, and a one-page checklist readers can adopt. No product pitch; one
> optional reference link, which we'd ask you to mark rel="sponsored" as we're the marketing partner of the
> organisation it points to — happy to drop it if you prefer.
>
> Would that be a fit for {publication}? I can send a full draft this week.
>
> Best,
> {name}, Optimize All Editorial

**Compliance note.** Optimize All is the official marketing partner of PCI AI. Per Google's guidance on links that
are part of a commercial relationship, the partner link must carry `rel="sponsored"` (or `rel="nofollow"`), and the
relationship is disclosed in the author bio. Never trade money or favours for a followed link.

---

## Article

AI has quietly entered the project forecast. Schedulers ask chat assistants to check logic, cost engineers use machine
learning to classify thousands of transactions, and project managers paste monthly reports into AI tools to "find the
risks". Much of this is useful. But in many organisations, nobody has decided what happens between an AI tool producing
a number and a steering committee acting on it.

That gap is a governance problem, not a technology problem. Here are five questions every PMO or project controls lead
should answer before AI touches a forecast that informs a decision.

### 1. Which tools may process which data?

Project data is rarely harmless. Cost reports contain commercial rates; correspondence contains contractual
positions; resource plans contain personal information. The first governance question is simply: which AI tools are
approved, and for which data classifications?

A one-page register is enough to start: tool name, approved uses, permitted data classifications, and owner. When a
planner wonders whether they can paste a subcontractor's claim into a chatbot, the answer should be written down — not
left to individual judgement under deadline pressure.

### 2. What data did the model use, and as of when?

A forecast without a basis is an opinion. For AI-assisted analysis, the basis must record which data was used, from
which system, as of which date, and any filters or exclusions applied. If the tool used historical projects to
generate a range, list them.

This is not bureaucracy. When the forecast is challenged — and good steering committees challenge forecasts — the
first question will be "based on what?". Without a recorded basis, the only honest answer is "we're not sure".

### 3. How does the AI output compare with an explainable method?

Every AI-assisted forecast should be compared with a simple, explainable calculation. In cost forecasting, the classic
benchmark is an earned-value estimate at completion: budget divided by the cost performance index. In scheduling, it
might be the critical-path forecast from the updated schedule.

If the AI output and the benchmark agree, the AI has added confidence and often a useful range. If they diverge
sharply, the divergence must be explained before anyone relies on the AI number. "The model says so" is not an
explanation.

**A worked example.** A 30-month project is 45% complete. Budget at completion is 80 million; earned value 32 million;
actual cost 36 million. The cost performance index is 0.89, giving a benchmark estimate at completion of about 90
million.

An AI tool analyses twelve similar completed projects and suggests a final cost between 86 and 96 million, median 90
million. The median agrees with the benchmark — reassuring. But the reviewer notices that three reference projects used
a contracting model in which the owner carried ground risk. Excluding them narrows the range to about 87–94 million.

The steering committee now receives: "Forecast 87–94 million, median 90 million, based on cost performance to date and
nine comparable completed projects; AI-assisted, reviewed by the controls lead." That sentence contains a basis, a
range, a benchmark and an accountable reviewer.

### 4. Who reviewed it, and what did they check?

AI-assisted outputs that inform decisions need a named human reviewer. The review does not need to be elaborate, but
it should be consistent. A short checklist works:

- Are the inputs complete and current?
- Does the output agree with the explainable benchmark? If not, why not?
- Are the reference data or assumptions appropriate for this project?
- Are there obvious errors — impossible dates, negative quantities, double counting?
- Is the output presented as a range with drivers, rather than a false-precision single point?

Record who reviewed, when and what they changed. Over time, the review log becomes evidence of how reliable each tool
is — and tells you where to tighten or relax the process.

### 5. How is AI use disclosed in reports?

Some teams worry that disclosing AI use will undermine confidence. In practice, the opposite tends to happen.
Stakeholders are already aware that AI tools exist; what erodes trust is discovering after the fact that a number came
from a tool nobody checked.

A single line in the report — "AI-assisted analysis of X; reviewed by Y; compared with Z" — signals control. It also
sets a norm across the project: AI is welcome, and so is accountability.

## Common objections — and answers

**"This will slow us down."** The review checklist takes minutes for routine outputs. What slows projects down is a
forecast that turns out to be wrong and has to be walked back in front of a board or a lender. Proportionality helps:
light review for internal working drafts, full review for anything that informs a decision.

**"Our people already check their work."** Almost certainly — informally. Governance makes the checks consistent,
visible and transferable. When a key analyst leaves, the review log and forecast basis stay.

**"The tools change every month."** That is exactly why the questions are tool-neutral. Which data may be processed,
how outputs are compared with a benchmark, who reviews them and how use is disclosed apply equally to today's chat
assistant and next year's forecasting platform.

**"We're too small for this."** A small team can run the whole approach in a shared document: one page for the tool
register, a forecast template with a basis section, and a checklist. Scale the formality with the size of the decisions,
not the size of the team.

## Where AI helps most — and least

In our experience, the highest-value, lowest-risk uses of AI in forecasting are preparatory: cleaning and reconciling
data, flagging anomalies such as progress claimed with no labour booked, drafting variance narratives for a human to
edit, and generating ranges from well-documented historical projects. The riskiest uses are those that replace agreed
rules with inference — for example, letting a model estimate progress percentages, or presenting an unreviewed AI number
as the forecast of record. Governance should make the first category easy and the second category impossible without
review.

## Putting it into practice

You do not need a large programme to start. In the next month:

1. **Week 1:** write the approved-tools register (question 1).
2. **Week 2:** add a "basis" section to your forecast template (question 2).
3. **Week 3:** require an explainable benchmark alongside any AI-assisted forecast (question 3).
4. **Week 4:** introduce the five-point review checklist and a disclosure line (questions 4 and 5).

Revisit after three reporting cycles. Keep what helps; simplify what doesn't.

## The skill behind the policy

Governance frameworks only work when practitioners understand both the fundamentals — scheduling, earned value,
forecasting, risk — and where AI helps or misleads. That combination is becoming a core expectation of project controls
roles, and professional bodies are starting to reflect it; for example, [a credential that builds governed AI into
project controls](https://pciai.org) now assesses these skills together in scenario-based exams.

Whatever route you take, the principle is the same: AI can make forecasts faster and richer, but accountability for
the number still belongs to people. Five questions, answered in writing, keep it that way.

---

## Author bio

*The Optimize All Editorial team writes practical guides on project controls, project finance and professional
certification, and publishes free courses through the Optimize All Academy. Optimize All is the official marketing
partner of PCI AI and Certuvo.*
