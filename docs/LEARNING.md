# Optimize All Academy — Learning module

Free courses for everyone (sales, marketing, SEO, AI, business, design, data and the platform itself) with lessons,
knowledge checks, a server-graded final exam, verifiable certificates, Open Badges and LinkedIn integration.

* **Public website** — `/learn` (catalog), `/learn/{course}` (course page), `/learn/{course}/{lesson}` (lesson). Every
  course and lesson is readable anonymously and indexable (schema.org JSON-LD, sitemap); knowledge checks work in the
  browser. Progress, the exam and the certificate need a free account (registration creates a normal participant).
* **Participant portal** — nav item **Learning** (`/app/learning`): "My learning" dashboard, catalog with progress,
  course pages, lesson player, exams, certificates. The participant home shows a Learning panel (continue, progress
  rings, certificates, recommendations); the profile shows earned badges.
* **Admin portal** — **Learning** (`/admin/learning`): courses with statistics, versions, question analytics, learners,
  CSV export, lesson videos, course editor, certificates (issue/revoke).
* **Public verification** — `/verify/certificates/{id}` (server-rendered by the API in production, see below).

Code: `backend/src/OptimizeAll.Domain/Learning` (entities, pack contract, exam rules — pure), `backend/src/OptimizeAll.Api/Modules/Learning`
(services, controllers, catalog packs, certificates), `frontend/src/features/learning` (shared UI),
`frontend/src/features/participant/learning`, `frontend/src/features/public/learn`, `frontend/src/features/admin/learning`.
API reference: [api/learning.md](api/learning.md). Security: [SECURITY.md § 14](SECURITY.md#14-learning-exam-integrity-and-credentials).

## 1. Data model

| Table | Entity | Purpose |
|---|---|---|
| `learning_courses` | `Course` | Stable identity (slug), origin (Pack / Admin), status (Draft / Published), `PublishedVersionId`, `LatestVersionId`, featured flag, sort order, and **listing columns copied from the published version** (title, subtitle, category, level, minutes, module/lesson counts, badge name, skills) so catalog queries never read lesson bodies. Concurrency-stamped. |
| `learning_course_versions` | `CourseVersion` | Immutable content versions: the full course document (course-pack JSON), SHA-256, number (1, 2, 3… per course), source (Pack with its pack version, or Admin with the version it was based on), note, author. Modules, lessons, knowledge checks and the question pool live in this document. |
| `learning_enrolments` | `Enrolment` | One per learner and course (unique). Resume point (`LastLessonSlug`), last activity, lessons-completed time, passed time, best score. |
| `learning_lesson_progress` | `LessonProgress` | Started/completed per enrolment and **lesson slug** (unique), so progress survives new content versions. |
| `learning_knowledge_check_answers` | `KnowledgeCheckAnswer` | The learner's latest answer per knowledge-check question (not graded for the certificate). |
| `learning_exam_attempts` | `ExamAttempt` | The attempt: course version, drawn questions + option order (`DrawJson`), saved answers (`AnswersJson`, original option indices), start/deadline/submitted, status (InProgress / Submitted / Expired), score, passed. `ActiveKey` (unique, null when finished) allows one attempt in progress per learner and course. |
| `learning_exam_answers` | `ExamAnswer` | Per-question outcome of every graded attempt (question id, selection, correct, attempt passed) — the source of question analytics. |
| `learning_certificates` | `Certificate` | Verification code `OA-XXXX-XXXX` (unique), snapshots (holder name, course title/slug, badge, skills, score), issue date, manual issuer, Open Badges recipient salt, revocation (time, staff, reason). `ActiveKey` (unique, null when revoked) allows one valid certificate per learner and course. Concurrency-stamped. |

Parsed versions are cached in memory by version id (`CourseContentCache`, immutable, LRU-bounded at 256); the catalog
(~50 courses) is paged from `learning_courses` only.

## 2. Course packs (contract)

Courses ship as JSON "course packs" in `backend/src/OptimizeAll.Api/Modules/Learning/Catalog/<slug>.json` (compiled
into the API as embedded resources — adding a file is enough). The same document format is used for every stored
version, including staff-authored ones.

```jsonc
{
  "slug": "social-selling-fundamentals",     // kebab-case, unique, = file name
  "version": 1,                              // bump when content changes materially
  "title": "…",                              // ≤ 80
  "subtitle": "…",                           // ≤ 140
  "category": "sales",                       // sales | marketing | seo | ai | business | design | data | platform
  "level": "beginner",                       // beginner | intermediate | advanced
  "estimatedMinutes": 120,
  "description": "Markdown, 80–200 words",
  "outcomes": ["5–8, each ≤ 140"],
  "skills": ["3–8 short tags"],              // certificate, Open Badge tags, LinkedIn
  "prerequisites": [],                       // other course slugs (shown as "Recommended first", not enforced)
  "badge": { "name": "≤ 60", "description": "30–80 words", "criteria": "…" },
  "passingScore": 80,                        // 50–100
  "modules": [{ "slug": "…", "title": "…", "summary": "…", "lessons": [{
    "slug": "…", "title": "…", "type": "article",          // article | video
    "durationMinutes": 8,
    "body": "Markdown, 500–1100 words, headings from ###, no HTML (except inside code), no external images",
    "video": null,                                          // video: { "script": "150–400 words", "src": null, "poster": null, "captions": null }
    "keyTakeaways": ["3–5"],
    "knowledgeCheck": [{ "question": "…", "options": ["2–5"], "correct": [1], "explanation": "…" }],  // 2–4
    "activity": "optional, ≤ 600 chars, or null"
  }]}],
  "finalExam": { "questionCount": 20, "timeLimitMinutes": 30, "maxAttemptsPerDay": 3, "pool": [{
    "id": "sell-001",                       // unique in the course
    "module": "why-social-selling",         // a module slug
    "difficulty": "easy",                   // easy | medium | hard
    "type": "single",                       // single (exactly 1 correct) | multiple (≥ 2 correct and ≥ 1 wrong)
    "question": "…", "options": ["3–5, unique, no 'all/none of the above'"], "correct": [2], "explanation": "…"
  }]}                                       // pool ≥ ceil(1.5 × questionCount) and covers every module
}
```

The contract is code: `CoursePackValidator` (Domain). Two modes:

* **Strict** — every rule above including the editorial word ranges. `UnitTests/Learning/CoursePackTests` validates
  **every pack in the catalog** in CI (plus file name = slug, unique slugs, prerequisites that exist, lossless
  round-trip), so content changes are checked on every push.
* **Authoring** — identical structure, count and MCQ rules (so the admin MCQ editor validates exactly like packs), minus
  the word ranges (description, badge description, lesson body, video script). Used by the admin editor and by the
  startup upsert (a pack failing it is skipped with an error log rather than breaking startup).

Unknown JSON properties are errors (typos never pass silently).

### Startup upsert and how pack updates and admin edits interact

`LearningCatalogSeeder` runs in the **Baseline** seed profile (every environment) under the named lock
`learning-catalog`, idempotently by slug + pack version:

1. New slug → a pack-origin course, version 1, **published**.
2. Known slug and a **higher** pack `version` → a new course version (source Pack). It is **published automatically
   only while the live version is itself pack-sourced**. If staff published an edited version, the pack update is
   stored but **not** published: the admin course list shows "Pack update", and staff compare versions and publish the
   one they want. Nothing is ever overwritten.
3. Same pack version with different content → ignored with a warning (**bump `version` to ship a change**).
4. A slug that belongs to a staff-authored course → skipped with a warning.

Staff edits (course editor, lesson videos) always create a **new version** based on the latest one; the pack file in the
repository is never modified. To make a staff edit permanent in the repository, export the version (admin API
`GET /admin/learning/courses/{id}/versions/{versionId}`), copy it into the pack file and bump `version`.

## 3. Lessons and progress

* Lessons render the pack's Markdown with the site's safe Markdown component (headings, lists, **GFM tables,
  blockquotes, task lists as read-only checkboxes**, code, links; no raw HTML).
* Video lessons show an accessible `<video>` (controls, poster, WebVTT captions track, full transcript = the narration
  script) when `video.src` is set; otherwise the article plus a "Video coming soon" note. Videos are produced from the
  narration script with ElevenLabs (voice) and HeyGen (avatar), then uploaded in the admin (Videos tab: MP4 up to 50 MB,
  poster image, captions) or linked by https URL (add the host to the web container's `MEDIA_SRC_EXTRA` so the CSP
  allows it).
* Opening a lesson records the resume point; "Mark lesson complete" records completion; knowledge-check answers are
  stored per question (latest answer). When every lesson of the published version is complete the exam unlocks.

## 4. Final exam rules

* **Start** (`POST /me/learning/courses/{slug}/exam/attempts`): enrolled, every lesson complete, no valid certificate,
  no attempt in progress (an abandoned attempt past its deadline is graded first), attempts in the rolling 24 hours below
  the course's `maxAttemptsPerDay`. The server draws `questionCount` distinct questions from the pool — one per module
  first when the count allows, the rest at random — and shuffles each question's options; the draw and the deadline
  (`start + timeLimitMinutes`) are stored with the attempt.
* **During** — the client shows one question at a time with a timer computed from the server's clock; each answer is
  autosaved (`PUT …/answers`) until the deadline + 30 s grace. No correct answers or explanations are sent.
* **Submit** — within the time limit the submitted answers are merged over the saved ones; after it only the saved
  answers are graded (status Expired). A question is correct only when the selected set equals the correct set;
  unanswered = wrong; score = floor(correct × 100 / questions); pass = score ≥ `passingScore`. Resubmitting returns the
  result (idempotent).
* **Review** — after submission: score, pass/fail, and per question the learner's answer, the correct options and the
  explanation. **Retake**: a new attempt with a new draw, within the daily budget; after a pass, no more attempts.

Pure rules (draw, grading, timer, budget) are in `Domain/Learning/ExamEngine.cs` with unit tests.

## 5. Certificates

Passing issues a certificate in the same transaction (in-app + email notification). Staff with `learning.certify` can
also **issue** one manually (confirm + reason, audited) and **revoke** one (confirm + reason + concurrency stamp,
audited, the holder is notified; one valid certificate per learner and course, a revoked one keeps its row).

* **Verification** — `GET /api/v1/public/learning/certificates/{id}` or `/verify?code=OA-XXXX-XXXX`: holder name,
  course, badge, skills, issue date, status valid/revoked (never the email).
* **Verification page** — `/verify/certificates/{id}` is server-rendered with the rest of the public site
  (`/_document`, [SEO_CRO.md](SEO_CRO.md) § Rendering: nginx `@document`, the Vite `seoShell` plugin): the
  `CertificateVerificationDto`'s title/description, canonical, **Open Graph and Twitter tags** (what LinkedIn reads for
  "Share"), schema.org `EducationalOccupationalCredential` JSON-LD and the verification itself, around the app shell,
  whose `features/public/learn/VerifyCertificatePage.tsx` then takes over. A revoked certificate's page is `noindex`.
  `/learn`, every course and every lesson are server-rendered the same way and listed in the `learn` child sitemap
  (`/sitemaps/learn.xml`), llms.txt and the admin SEO overview; titles stay within 60 and descriptions within 155
  characters.
* **PDF** — `…/certificate.pdf`: A4 landscape vector certificate rendered with **PDFsharp 6.2.4** (MIT licence,
  pure managed, no native dependencies) with embedded **Work Sans** and **Lora** fonts (SIL Open Font License 1.1, licence
  files in `Modules/Learning/Fonts`), so it renders identically on Render's Linux containers with no system fonts or
  packages. Metadata: title, author (issuer), subject (with the verification code), keywords (skills). QuestPDF was not
  used: its Community licence is limited by company revenue. A revoked certificate's PDF carries a "REVOKED" stamp.
* **Image** — `…/certificate.svg` (1600×1131, same layout) and the course badge `…/courses/{slug}/badge.svg` (400×400,
  brand navy + amber with the category colour, badge name, level, issuer). Raster (PNG) output is not generated (it would
  need a native imaging library); social previews use the site's `og-image.png`.

## 6. Badges: Open Badges 2.0 (hosted)

Each certificate is also an **Open Badges 2.0 hosted assertion** (IMS Global / 1EdTech OB 2.0 Final). OB 2.0 hosted
verification was chosen over OB 3.0 (W3C Verifiable Credentials) because 3.0 credentials must carry a cryptographic proof
(signing keys, key rotation, DID/JWK publication); hosted 2.0 assertions are verified by fetching their URL and are
accepted by badge backpacks and validators.

| Document | URL |
|---|---|
| Issuer `Profile` | `/api/v1/public/learning/openbadges/issuer` |
| `BadgeClass` (per course: name, description, image = badge SVG, criteria = course page + narrative, tags = skills) | `/api/v1/public/learning/openbadges/badges/{slug}` |
| `Assertion` (per certificate: hashed email recipient `sha256$…` with a per-assertion salt, badge, `verification.type = hosted`, issuedOn, evidence = verification page) | `/api/v1/public/learning/openbadges/assertions/{id}` |

Served as `application/ld+json` with CORS `*`. A revoked assertion answers **410 Gone** with the spec's stub
(`id`, `revoked: true`, `revocationReason`) inside a problem document. `UnitTests/Learning/CredentialTests` validates all
three documents against JSON Schemas transcribed from the OB 2.0 data model (`UnitTests/Learning/OpenBadgesSchemas`,
JsonSchema.Net 7.4, MIT).

## 7. LinkedIn

No LinkedIn API or credentials are needed.

* **Add to profile** — LinkedIn's documented "Add to Profile" URL for certifications:
  `https://www.linkedin.com/profile/add?startTask=CERTIFICATION_NAME&name={badge name}&organizationId={company page id}&issueYear={yyyy}&issueMonth={m}&certUrl={verification URL}&certId={OA-XXXX-XXXX}`.
  When no company page id is configured, `organizationName={issuer name}` is sent instead of `organizationId` (LinkedIn
  then shows the name without a logo). `expirationYear`/`expirationMonth` are omitted: certificates do not expire.
  Parameter names follow LinkedIn's Add-to-Profile documentation (verified offline against the published format; LinkedIn
  may change it — the builder is one function, `Certificates/LearningLinks.cs` → `LinkedIn.AddToProfile`, with a unit test).
* **Share** — `https://www.linkedin.com/sharing/share-offsite/?url={verification URL}`; LinkedIn reads the Open Graph
  tags of the server-rendered verification page.
* **Settings** — Admin → Settings → Learning: `learning.issuerName` (default "Optimize All Academy") and
  `learning.linkedInOrganizationId` (digits from the company page admin URL; empty = send the name). Audited like every
  setting.

## 8. SEO

* `/learn` (catalog), course pages and lesson pages are public, crawlable and listed in the XML sitemap
  (`IPublicSitemapContributor`, `Modules/Website/Public/SitemapContributors.cs`, implemented by
  `LearningSitemapContributor`).
* Course pages: schema.org `Course` (`provider`, `offers` price 0 / `isAccessibleForFree`, `hasCourseInstance` with
  `courseMode: Online` and `courseWorkload`, `syllabusSections`, `teaches`, `educationalCredentialAwarded`) +
  `BreadcrumbList`. Lessons: `LearningResource` + `Article` (+ `VideoObject` when a produced video has a poster) +
  `BreadcrumbList`. Titles and descriptions come from the course data (`seo` field of each DTO).
* The SPA writes these through the site head manager (`useDocumentHead`). *Hook point for server-side rendering:* the
  API DTOs already carry `seo` + `jsonLd`, so a server renderer can emit the same head for `/learn/**` without new
  endpoints.

## 9. Admin

Permissions (`Common/Security/Permissions.cs`, usable by custom roles): `learning.view` (lists, statistics, analytics,
learners, certificates, CSV export), `learning.manage` (author/edit/publish/unpublish, videos, media upload),
`learning.certify` (sensitive: issue/revoke). Built-in: Admin has all; Campaign manager has view + manage.

* **Courses** — enrolments, completion rate (passed ÷ enrolled), graded attempts, pass rate, average score, valid
  certificates, version and "pack update" flags; featured flag and sort order.
* **Question analytics** — per pool question: answers, % correct (difficulty) and discrimination (percentage points
  of % correct among passing attempts minus among the others; low or negative values flag questions to review).
* **Learners** — progress per learner (lessons, attempts, best score, certificate); results **CSV** (audited export).
* **Editor** — structured editor for the whole course document (course fields, modules, lessons with Markdown,
  knowledge checks and the exam pool through one MCQ editor) plus JSON import; "Validate" runs the server-side contract
  (Authoring mode) and lists issues by path; saving creates a new version (optionally published).
* **Videos** — per video lesson: upload MP4/poster/captions (Files module, `FilePurpose.LearningMedia`, public) or paste
  https URLs; saves a new version.

### Partner slots (integration point)

`frontend/src/features/learning/components/LearnSlot.tsx` is a no-op insertion point placed on the course page, lesson
page, exam result, certificate page, public verification page and the participant Learning panel, each with its slot
name (`learn.course` | `learn.lesson` | `learn.exam` | `learn.certificate` | `learn.dashboard`), keywords (course skills +
category label, `courseKeywords()` in `features/learning/api.ts`) and categories. The partners feature swaps its body for
`<PartnerSlot … />`. The public `/learn` pages render inside the public site layout, so site-wide providers (e.g.
sponsored-link handling of Markdown links) apply to lessons.

## 10. Demo data

The Demo seed (`LearningDemoSeeder`, after the main demo) features the platform course and the first course of other
categories, gives Sara a completed "Getting started on Optimize All" (a failed attempt, then a pass) with a valid
certificate plus progress in up to two more courses, starts the new participant on the platform course, and gives eight
more demo participants enrolments, progress and attempts so the admin statistics are real. See [DEMO.md](DEMO.md).

## 11. Tests

* Unit: `UnitTests/Learning/CoursePackTests` (every pack, validator rules), `ExamEngineTests` (draw, grading, timer,
  budget), `CredentialTests` (LinkedIn URL, Open Badges schemas, SVG escaping, PDF rendering, verification page encoding).
* Integration: `IntegrationTests/Learning/LearningTests` (catalog upsert and versioning, public endpoints and JSON-LD,
  enrolment/progress/knowledge checks, the full exam flow with fail/retake/pass, integrity — no answer leakage, one
  attempt, server timer, daily budget —, tenancy, impersonation, certificates, PDF, Open Badges, revocation, admin
  authoring/publishing/analytics/export, manual issue and the LinkedIn setting); `Seed/DemoSeedTests`; the API contract
  suite covers every new endpoint.
* Frontend: `features/learning/learning.test.tsx` (vitest + axe); E2E `frontend/e2e/j-learning`
  (`E2E_SUITE=j-learning scripts/e2e-journeys.sh`).
