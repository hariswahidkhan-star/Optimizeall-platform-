# API: Learning (academy, exams, certificates, admin)

All routes are under `/api/v1`. Conventions (camelCase JSON, string enums, UTC timestamps, RFC 7807 problems with
`code` + `traceId`, paging `?page&pageSize&search&sort&desc`, `concurrencyStamp`) are described in
[accounts-social-content-notifications-support-admin.md](accounts-social-content-notifications-support-admin.md).
Concepts, the course pack contract and the exam rules: [../LEARNING.md](../LEARNING.md). Machine-readable:
[openapi.json](openapi.json).

Enums: `CourseCategory` Sales | Marketing | Seo | Ai | Business | Design | Data | Platform · `CourseLevel` Beginner |
Intermediate | Advanced · `LessonType` Article | Video · `QuestionType` Single | Multiple · `ExamAttemptStatus`
InProgress | Submitted | Expired · `CourseStatus` Draft | Published · `CourseSource` Pack | Admin.

## 1. Public academy (anonymous, `public` rate limit)

| Endpoint | Notes |
|---|---|
| `GET /public/learning/courses` | Published courses → `PagedResult<CourseCard>`. Query: `category`, `level`, `maxMinutes` (1–6000), `featured`, `search`, `sort` (`title` \| `newest` \| `duration` \| `level`; default featured → sort order → title), `desc`, `page`, `pageSize`. |
| `GET /public/learning/categories` | Categories with published courses and counts. |
| `GET /public/learning/courses/{slug}` | `CourseDetail`: card, description (Markdown), outcomes, prerequisites (recommended), badge, exam rules, modules with lesson summaries, `seo`, `jsonLd` (schema.org `Course` + `BreadcrumbList`). 404 `course.not_found`. |
| `GET /public/learning/courses/{slug}/lessons/{lessonSlug}` | `Lesson`: body (Markdown), video (`src`/`poster`/`captions`/`transcript`, video lessons only), key takeaways, knowledge check **with answers and explanations** (not graded), activity, previous/next, position, `seo`, `jsonLd`. 404 `lesson.not_found`. |
| `GET /public/learning/courses/{slug}/badge.svg` | Course badge (`image/svg+xml`, cacheable, CORP cross-origin). |
| `GET /public/learning/summary` | The academy in numbers for the marketing pages and the header (`Cache-Control: public, max-age=300`): `{ courseCount, lessonCount, totalMinutes, pathCount, categories, featuredSlugs, highlights (≤ 8 course cards, featured subject courses first), skills (≤ 36), updatedAt }`. |
| `GET /public/learning/paths` | Learning paths with ≥ 1 published course → `{ paths: PathCard[], seo, jsonLd }` (card: slug, title, subtitle, level, courseCount, lessonCount, totalMinutes, categories, badges). |
| `GET /public/learning/paths/{slug}` | `PathDetail`: card, description (Markdown), outcomes, audience, `courses` (ordered `{ position, course: CourseCard }`, unpublished/planned slugs skipped), `seo`, `jsonLd` (`ItemList` of `Course`, `BreadcrumbList`, one `EducationalOccupationalCredential` per badge). 404 when unknown or empty. |

Pack v2 fields in the course and lesson DTOs: `CourseDetail.lastReviewed` (`"2026-09"` or null), `tools`, `lectureMinutes`,
`lectureCount`; `LessonSummary.hasLecture`, `lectureMinutes`; `Lesson.lastReviewed` and `Lesson.lecture` =
`{ title, targetMinutes, totalSeconds, produced, src, poster, captions, chapters: [{ index, title, points, narration,
startSeconds, seconds }], transcriptWords, youTubeId, embedUrl, publishedAt }` (null for lessons without a lecture).
| `GET /public/learning/certificates/{id}` | `CertificateVerification`: status `valid`/`revoked`, holder name, course, badge (+ description, criteria), skills, issue/revocation dates, issuer, links, `seo`, `jsonLd`. |
| `GET /public/learning/certificates/verify?code=OA-XXXX-XXXX` | Same, by verification code (case-insensitive, `OA-` optional). |
| `GET /public/learning/certificates/{id}/page` | Server-rendered verification page (`text/html`, OG/Twitter tags, JSON-LD); served at `/verify/certificates/{id}` by nginx. |
| `GET /public/learning/certificates/{id}/certificate.pdf` | Certificate PDF (A4 landscape, `Content-Disposition` `certificate-OA-….pdf`). |
| `GET /public/learning/certificates/{id}/certificate.svg` | Certificate image (SVG). |
| `GET /public/learning/openbadges/issuer` | Open Badges 2.0 issuer `Profile` (`application/ld+json`, CORS `*`). |
| `GET /public/learning/openbadges/badges/{slug}` | Open Badges 2.0 `BadgeClass`. |
| `GET /public/learning/openbadges/assertions/{id}` | Open Badges 2.0 hosted `Assertion`; revoked → **410** problem `learning.certificate_revoked` with `revoked: true`. |

## 2. My learning (`participant.portal`; writes denied while impersonating)

| Endpoint | Notes |
|---|---|
| `GET /me/learning` | Dashboard: stats, `continue`, `inProgress`, `completed`, `recommended` (≤ 4), `certificates`. |
| `GET /me/learning/courses` | Catalog (same query as the public one) → `PagedResult<MyCourseCard>` (course + enrolled, progress %, passed, certificate id). |
| `GET /me/learning/courses/{slug}` | `{ course: CourseDetail, progress: CourseProgress \| null }`. |
| `POST /me/learning/courses/{slug}/enrol` | Free; **201** when created, **200** when already enrolled. Rate limit `learning`. |
| `GET /me/learning/courses/{slug}/lessons/{lessonSlug}` | `{ lesson, enrolled, completed, answers, progress }`. |
| `POST /me/learning/courses/{slug}/lessons/{lessonSlug}/start` | Resume point → `CourseProgress`. 409 `learning.not_enrolled`. |
| `POST /me/learning/courses/{slug}/lessons/{lessonSlug}/complete` | → `CourseProgress` (`examUnlocked` once every lesson is complete). |
| `POST /me/learning/courses/{slug}/lessons/{lessonSlug}/checks/{index}` | Body `{ "selected": [0] }` (1–10 option indices) → `{ index, selected, isCorrect, correct, explanation }`. 400 `learning.invalid_answer`, 404 unknown question. |
| `GET /me/learning/paths` · `GET /me/learning/paths/{slug}` | Learning paths with the caller's progress: `{ card, progress }[]` / `{ path: PathDetail, progress }`; progress = `{ slug, completedCourses, courseCount, progressPercent, nextCourseSlug, started, courses: [{ slug, enrolled, progressPercent, passed, certificateId }] }`. |
| `GET /me/learning/certificates` · `GET /me/learning/certificates/{certificateId}` | The caller's certificates with `links` (verification, PDF, image, badge image, Open Badge assertion, LinkedIn add-to-profile, LinkedIn share). Others' → 404. |

## 3. Exams (`participant.portal`; writes denied while impersonating)

| Endpoint | Notes |
|---|---|
| `GET /me/learning/courses/{slug}/exam` | Rules, `attemptsRemaining` (rolling 24 h), `nextAttemptAt`, `activeAttemptId`, `canStart`, `blockedReason` (`learning.not_enrolled` \| `learning.lessons_incomplete` \| `learning.already_certified` \| `learning.attempt_in_progress` \| `learning.attempt_limit`), past attempts. |
| `POST /me/learning/courses/{slug}/exam/attempts` | **201** `ExamAttempt` (questions in shuffled order, no answers). 409 with the codes above. Rate limit `submissions`. |
| `GET /me/learning/attempts/{attemptId}` | In progress: questions, saved `selected` positions, `secondsRemaining`, `deadlineAt`, `serverNow`. Finished: `result` + per-question `review` (`isCorrect`, `correct` positions, `explanation`). Others' → 404. |
| `PUT /me/learning/attempts/{attemptId}/answers` | Body `{ "questionId": "…", "selected": [shown positions] }` (empty = clear). 400 `learning.unknown_question` / `learning.invalid_answer` (out of range, duplicate, >1 for single); 409 `learning.attempt_closed`, `learning.time_expired`. |
| `POST /me/learning/attempts/{attemptId}/submit` | Body `{ "answers": [ { "questionId", "selected" } ] }` (optional, merged over saved answers within the time limit; ignored after it). Returns the graded attempt; idempotent. Passing issues the certificate (`result.certificateId`). |

## 4. Learning admin

`learning.view` unless noted; writes need `learning.manage`; certificate issue/revoke need `learning.certify` (denied while impersonating, audited).

| Endpoint | Notes |
|---|---|
| `GET /admin/learning/courses` | `PagedResult<AdminCourseRow>` with enrolments, completions, completion/pass rate, attempts, average score, certificates, version numbers, `packUpdateAvailable`, `concurrencyStamp`. Query: `category`, `status`, `search`, `sort` (`title` \| `updated`). |
| `GET /admin/learning/courses/{courseId}` | Row + versions (+ pack file name). |
| `GET /admin/learning/courses/{courseId}/versions/{versionId}` *(manage)* | Full document (answers included). |
| `GET /admin/learning/courses/{courseId}/questions` | `QuestionStat[]`: answered, correct, % correct, % correct among passers/others, discrimination (points). |
| `GET /admin/learning/courses/{courseId}/learners` | `PagedResult<LearnerRow>`; `passed`, `search`. |
| `GET /admin/learning/courses/{courseId}/results.csv` | CSV (formula-safe), audited `learning.results_exported`. |
| `GET /admin/learning/users/{userId}` | A person's enrolments and certificates. |
| `POST /admin/learning/courses/validate` *(manage)* | `{ document }` → `ValidationReport { valid, issues[{path, message}], moduleCount, lessonCount, poolSize, questionCount }`. |
| `POST /admin/learning/courses` *(manage)* | `{ document, publish }` → **201**. 400 `learning.invalid_course` (errors keyed `document.<path>`), 409 `learning.slug_taken`. |
| `POST /admin/learning/courses/{courseId}/versions` *(manage)* | `{ document, basedOnVersionId?, note?, publish, concurrencyStamp }` → new version. 400 `learning.slug_immutable`. |
| `POST /admin/learning/courses/{courseId}/publish` *(manage)* | `{ versionId, concurrencyStamp }`. |
| `POST /admin/learning/courses/{courseId}/unpublish` *(manage)* | `{ concurrencyStamp }`. |
| `PUT /admin/learning/courses/{courseId}/settings` *(manage)* | `{ isFeatured, sortOrder (0–10000), concurrencyStamp }`. |
| `PUT /admin/learning/courses/{courseId}/lessons/{lessonSlug}/video` *(manage)* | `{ src?, poster?, captions?, publishedAt?, publish, concurrencyStamp }` (uploads `/api/v1/files/{id}` or https; for a lesson with a v2 lecture this sets the **lecture** media — `src` typically a YouTube URL — and `publishedAt`) → new version. 409 `learning.not_a_video_lesson` (no lecture and not a video lesson). |
| `POST /admin/learning/media` *(manage)* | Multipart `file`: MP4 ≤ 50 MB, WebVTT ≤ 1 MB, PNG/JPEG/WebP poster ≤ 10 MB → **201** `StoredFile` (`url` = `/api/v1/files/{id}`, public). |
| `GET /admin/learning/certificates` | `PagedResult<AdminCertificate>`; `courseId`, `revoked`, `search` (holder, email, code, course). |
| `POST /admin/learning/certificates` *(certify)* | `{ userId, courseId, reason, confirm: true }` → **201**. 409 `learning.already_certified`. |
| `POST /admin/learning/certificates/{certificateId}/revoke` *(certify)* | `{ reason, confirm: true, concurrencyStamp }`. 409 `learning.certificate_revoked`, `concurrency.conflict`. |

Settings (`PUT /admin/settings/{key}`, `settings.manage`): `learning.issuerName` (string, 2–100),
`learning.linkedInOrganizationId` (digits or empty).
