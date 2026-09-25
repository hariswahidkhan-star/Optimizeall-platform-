---
slug: cisa-exam-preparation-guide
title: CISA Exam Preparation Guide for IT Auditors
description: CISA exam preparation for IT auditors: the five domains, thinking like an auditor, a worked question, COBIT 2019 context and a ten-week study plan.
cluster: certuvo
primaryKeyword: CISA exam preparation
categories: exam-prep
tags: cisa, it audit, governance, exam prep
related: cia-exam-preparation-guide, spaced-repetition-and-active-recall-for-exams, how-to-use-an-ai-study-coach
publishedDaysAgo: 7
cover: exam-prep
coverAlt: Purple cover with amber accent bars and the words Certification exam prep
---
The Certified Information Systems Auditor (CISA) credential from ISACA is one of the most widely recognised qualifications in IT audit, assurance and control. Many candidates arrive from strong technical backgrounds — networking, security, development — and are surprised by the exam. It rarely asks how to configure something. It asks what an **auditor** should conclude, recommend or do first.

This guide explains the domains, the auditor mindset the exam rewards, a worked question, how frameworks such as COBIT 2019 fit, and a ten-week study plan.

> ISACA sets the job practice areas, domain weights, question counts, timing, scoring, fees and experience requirements, and updates them periodically. Check ISACA's current exam content outline and certification requirements before planning.

## The five domains

The CISA job practice is organised into five domains:

1. **Information System Auditing Process** — planning and conducting audits, risk-based audit approaches, evidence, sampling, reporting and follow-up.
2. **Governance and Management of IT** — IT strategy, frameworks, policies, organisational structure, risk management and performance monitoring.
3. **Information Systems Acquisition, Development and Implementation** — project governance, business cases, system development methods, testing, controls in development and implementation, and post-implementation review.
4. **Information Systems Operations and Business Resilience** — IT operations, incident and problem management, change and configuration management, business continuity and disaster recovery.
5. **Protection of Information Assets** — security frameworks, identity and access management, network and endpoint security, data classification and privacy.

Check ISACA's current outline for the domain weights and any changes to subtopics.

## Think like an auditor, not an engineer

Technical candidates often pick the answer that *fixes* the problem. The CISA exam usually wants the answer that reflects the auditor's role: independent, risk-based and evidence-driven. Useful rules of thumb:

- **Risk first.** When asked what to do first, the answer is often to understand or assess risk before testing or recommending.
- **Auditors recommend; management implements.** Options where the auditor designs or implements a control usually compromise independence.
- **Evidence quality matters.** Evidence obtained directly by the auditor (observation, re-performance) is generally stronger than evidence provided by the auditee.
- **Preventive beats detective beats corrective**, all else being equal — but the *best* control is the one that addresses the specific risk described.
- **Governance and accountability.** Look for answers that put responsibility with the right owner (the board, senior management, the data owner).

## A worked question

*During an audit of a payroll application, an IS auditor finds that developers have access to the production environment and can move code into production without independent review. What should the auditor do FIRST?*

- A. Remove the developers' production access.
- B. Recommend implementing an automated change-management tool.
- C. Determine whether compensating controls exist and assess the risk of unauthorised changes.
- D. Report the finding to the audit committee immediately.

**Reasoning:** A is management's action, not the auditor's — doing it would impair independence. B jumps to a solution before understanding the risk. D may be appropriate later, depending on significance, but reporting before evaluating is premature. **C is correct:** the auditor evaluates whether compensating controls (for example, independent post-implementation review of changes, or monitoring of production changes) reduce the risk, and then determines the significance of the finding.

The pattern — understand risk and existing controls before recommending or reporting — recurs throughout the exam.

## Where COBIT 2019 fits

COBIT 2019, ISACA's framework for the governance and management of enterprise information and technology, gives useful structure for Domain 2 and beyond. You do not need to memorise it exhaustively for the exam, but you should understand:

- the distinction between **governance** (evaluate, direct, monitor — typically the board's role) and **management** (plan, build, run, monitor);
- the idea of **governance and management objectives** covering areas such as risk, change, security and continuity;
- how frameworks help auditors define criteria against which to assess controls.

## A ten-week study plan

At around 10–12 hours a week:

| Week | Focus |
|---|---|
| 1 | Read the exam content outline; diagnostic practice set; start an error log |
| 2–3 | Domain 1: audit planning, risk-based auditing, evidence, sampling, reporting |
| 4 | Domain 2: IT governance, frameworks (including COBIT 2019 concepts), risk management |
| 5 | Domain 3: acquisition, development, testing, implementation, post-implementation review |
| 6 | Domain 4: operations, change and incident management, business continuity and disaster recovery |
| 7–8 | Domain 5: security frameworks, access management, network security, data protection |
| 9 | Mixed timed practice across all domains; error-log review |
| 10 | Full timed practice exam; targeted review; rest |

Every week, do a mixed question set that includes earlier domains, and log each error by **type**: knowledge gap, role confusion (engineer vs auditor), misread "first/best/most", or evidence-quality mistake.

## Evidence and sampling: a quick primer

Domain 1 questions frequently turn on evidence and sampling, and they are easy marks once the logic is clear.

**Evidence reliability.** As a rule of thumb, evidence is more reliable when it is obtained directly by the auditor, comes from an independent source, is documented rather than oral, and is generated by a system with effective controls. Re-performing a control is stronger than reading a procedure that describes it; a confirmation from a third party is stronger than a spreadsheet prepared by the auditee.

**Sampling choices.** Statistical sampling lets the auditor quantify sampling risk and draw conclusions about a population; judgemental sampling relies on the auditor's selection and cannot be projected statistically. Attribute sampling suits tests of controls (does the control operate — yes or no?), while variable sampling suits tests of amounts. Questions often ask which approach fits a stated objective.

**Data analytics.** Where the entire population can be analysed with audit software — for example, every change ticket or every access record — full-population testing can replace sampling and give stronger assurance. Expect scenarios that ask when analytics are the better choice.

**Worked mini-example.** An auditor wants to test whether every production change in the year had documented approval. There are 4,000 changes in the ticketing system, which exports cleanly. The strongest approach is to analyse all 4,000 records for missing approvals using data analytics, then investigate exceptions, rather than drawing a small judgemental sample.

## Memory work that pays off

Some topics reward precise recall: continuity and recovery terms (recovery time objective vs recovery point objective), sampling concepts, types of evidence, control categories, and system development phases. Put these on flashcards and review them on an expanding schedule — see [Spaced repetition and active recall for professional exams](/blog/spaced-repetition-and-active-recall-for-exams).

## CISA and CIA: how they relate

The CIA (from the IIA) covers internal auditing broadly — governance, risk, control and the internal audit function. CISA focuses on information systems audit, control and security. Many audit professionals hold both, and the auditor mindset transfers between them. See our [CIA exam preparation guide](/blog/cia-exam-preparation-guide).

## Tools that help

A blueprint-mapped question bank with explanations that expose the auditor mindset is especially valuable for CISA. [Certuvo](https://certuvo.com) offers CISA preparation with exam-style questions written and verified by qualified professionals and mapped to the official blueprint, and an AI Coach that references standards such as COBIT 2019 and uses the Socratic method to help you reason like an auditor — automatically disabled during mock exams. See our [Certuvo partner page](/partners/certuvo).

Optimize All's free courses [Professional Certification Exam Success](/learn/professional-certification-exam-success) and [AI for Data Analysis and Decision-Making](/learn/ai-for-data-analysis-and-decision-making) complement the audit and data topics.

## Frequently asked questions

### Is the CISA exam technical?

It requires technical understanding, but questions are framed from an auditor's perspective: assessing risk, evaluating controls and evidence, and recommending improvements. Deep configuration knowledge is rarely what earns the mark.

### What experience do I need for CISA?

ISACA requires relevant work experience for certification, with some substitutions and waivers available. Check ISACA's current requirements, including the timeframe for completing experience after passing the exam.

### How should I answer "first" questions?

Usually by choosing the option that gains understanding of risk and existing controls before testing, recommending or reporting. Avoid options where the auditor implements controls.

### Should I study COBIT 2019 in detail?

Understand its core concepts — governance vs management and the governance and management objectives — and how frameworks provide audit criteria. Check ISACA's outline for the expected depth.

---

*Optimize All is the official marketing partner of PCI AI and Certuvo.*
