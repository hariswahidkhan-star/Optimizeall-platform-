import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { json, mockFetch, problem, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import { AcademyCoursePage, AcademyLessonPage, AcademyPage } from '@/features/public/learn/AcademyPages';
import { VerifyCertificatePage } from '@/features/public/learn/VerifyCertificatePage';
import { ExamAttemptPage, ExamOverviewPage } from '@/features/participant/learning/ExamPages';
import { LearningCertificatePage, LearningHomePage } from '@/features/participant/learning/LearningHomePages';
import { LearningLessonPage } from '@/features/participant/learning/CoursePages';
import type { CourseCard, CourseDetail, ExamAttempt, Lesson, MyCertificate } from './api';

const card: CourseCard = {
  id: 'c1',
  slug: 'platform-getting-started',
  title: 'Getting started on Optimize All',
  subtitle: 'How campaigns work',
  category: 'Platform',
  level: 'Beginner',
  estimatedMinutes: 45,
  moduleCount: 2,
  lessonCount: 2,
  badgeName: 'Certified Creator',
  skills: ['Disclosure'],
  isFeatured: true,
  isNew: true,
  badgeImageUrl: '/api/v1/public/learning/courses/platform-getting-started/badge.svg',
  publishedAt: '2026-09-20T00:00:00Z',
};

const seo = { title: 'Getting started', description: 'Desc', canonicalPath: '/learn/platform-getting-started', imageUrl: null, noIndex: false };

const course: CourseDetail = {
  card,
  description: 'A **free** course.',
  outcomes: ['Explain campaigns'],
  prerequisites: [],
  badge: { name: 'Certified Creator', description: 'Knows the platform.', criteria: 'Pass the exam.', imageUrl: card.badgeImageUrl },
  exam: { questionCount: 10, timeLimitMinutes: 15, maxAttemptsPerDay: 3, passingScore: 80 },
  modules: [
    {
      slug: 'm1',
      title: 'How it works',
      summary: 'Basics.',
      lessons: [
        { slug: 'l1', title: 'Campaigns and eligibility', type: 'Article', durationMinutes: 10, hasVideo: false, hasLecture: false, lectureMinutes: 0 },
        { slug: 'l2', title: 'Submitting posts', type: 'Video', durationMinutes: 12, hasVideo: false, hasLecture: false, lectureMinutes: 0 },
      ],
    },
  ],
  version: 1,
  updatedAt: '2026-09-20T00:00:00Z',
  seo,
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'Course', name: 'Getting started', isAccessibleForFree: true }],
  lastReviewed: null,
  tools: [],
  lectureMinutes: 0,
  lectureCount: 0,
};

const lesson: Lesson = {
  courseSlug: card.slug,
  courseTitle: card.title,
  category: 'Platform',
  moduleSlug: 'm1',
  moduleTitle: 'How it works',
  slug: 'l2',
  title: 'Submitting posts',
  type: 'Video',
  durationMinutes: 12,
  body: '### Proof\n\nSubmit the **public link**.\n\n- [ ] Link opens logged out\n- [x] Disclosure visible\n\n| Reason | Fix |\n|---|---|\n| Missing ad | Add #ad |\n\n> Keep posts live.',
  video: { src: null, poster: null, captions: null, transcript: 'Hi and welcome back.' },
  keyTakeaways: ['The link is the proof.'],
  knowledgeCheck: [
    { index: 0, question: 'What is the proof?', options: ['The public link', 'A screenshot'], correct: [0], explanation: 'The link shows the live post.', multiple: false },
  ],
  activity: 'Check a link while logged out.',
  previous: { slug: 'l1', title: 'Campaigns and eligibility' },
  next: null,
  position: 2,
  lessonCount: 2,
  seo,
  jsonLd: [],
  lecture: null,
  lastReviewed: null,
};

const certificate: MyCertificate = {
  id: 'cert-1',
  verificationCode: 'OA-ABCD-2345',
  courseSlug: card.slug,
  courseTitle: card.title,
  badgeName: 'Certified Creator',
  skills: ['Disclosure'],
  score: 90,
  issuedAt: '2026-09-21T10:00:00Z',
  revoked: false,
  revokedAt: null,
  links: {
    verificationUrl: 'https://app.example/verify/certificates/cert-1',
    pdfUrl: '/api/v1/public/learning/certificates/cert-1/certificate.pdf',
    imageUrl: '/api/v1/public/learning/certificates/cert-1/certificate.svg',
    badgeImageUrl: card.badgeImageUrl,
    openBadgeAssertionUrl: 'https://app.example/api/v1/public/learning/openbadges/assertions/cert-1',
    linkedInAddToProfileUrl:
      'https://www.linkedin.com/profile/add?startTask=CERTIFICATION_NAME&name=Certified%20Creator&organizationName=Optimize%20All%20Academy&issueYear=2026&issueMonth=9&certUrl=https%3A%2F%2Fapp.example%2Fverify%2Fcertificates%2Fcert-1&certId=OA-ABCD-2345',
    linkedInShareUrl: 'https://www.linkedin.com/sharing/share-offsite/?url=https%3A%2F%2Fapp.example%2Fverify%2Fcertificates%2Fcert-1',
  },
};

const attempt: ExamAttempt = {
  id: 'a1',
  courseSlug: card.slug,
  courseTitle: card.title,
  status: 'InProgress',
  startedAt: new Date().toISOString(),
  deadlineAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  serverNow: new Date().toISOString(),
  secondsRemaining: 900,
  questionCount: 2,
  passingScore: 80,
  questions: [
    { id: 'q1', number: 1, type: 'Single', question: 'Which link is proof?', options: ['Public link', 'Screenshot'], selected: [], review: null },
    { id: 'q2', number: 2, type: 'Multiple', question: 'Which break the rules?', options: ['Bought followers', 'Approved caption', 'Duplicates'], selected: [], review: null },
  ],
  result: null,
};

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.refresh_invalid', 'No session') };
const signedIn = { 'POST /auth/refresh': () => json(200, session()) };
const catalogRoutes = {
  'GET /public/learning/courses': () => json(200, { items: [card], total: 1, page: 1, pageSize: 24, totalPages: 1 }),
  'GET /public/learning/categories': () => json(200, [{ category: 'Platform', label: 'Optimize All platform', courseCount: 1 }]),
  'GET /public/learning/courses/platform-getting-started': () => json(200, course),
  'GET /public/learning/courses/platform-getting-started/lessons/l2': () => json(200, lesson),
  'GET /public/site': () => problem(404, 'http_404', 'Not found'),
  'GET /public/learning/paths': () => json(200, { paths: [], seo, jsonLd: [] }),
};

describe('public academy', () => {
  it('lists courses with category filters and no axe violations', async () => {
    mockFetch({ ...anonymous, ...catalogRoutes });
    const { container } = renderWithApp(<AcademyPage />, { route: '/learn', path: '/learn', withAuth: true });
    const links = await screen.findAllByRole('link', { name: card.title });
    links.forEach((l) => expect(l).toHaveAttribute('href', '/learn/platform-getting-started'));
    const filters = screen.getByRole('group', { name: 'Filter by category' });
    expect(within(filters).getByRole('button', { name: /Optimize All platform/ })).toHaveAttribute('aria-pressed', 'false');
    // The hub: subject tiles (animated illustrations) filter the catalog.
    expect(screen.getByRole('button', { name: /Optimize All platform\s*1 course/ })).toBeInTheDocument();
    expect(screen.getAllByText('Featured', { selector: '.lx-flag' }).length).toBeGreaterThan(0);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('renders the course page with its syllabus, certificate preview and JSON-LD', async () => {
    mockFetch({ ...anonymous, ...catalogRoutes });
    const { container } = renderWithApp(<AcademyCoursePage />, { route: '/learn/platform-getting-started', path: '/learn/:slug' });
    expect(await screen.findByRole('heading', { level: 1, name: card.title })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /How it works/ })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('link', { name: 'Submitting posts' })).toHaveAttribute('href', '/learn/platform-getting-started/l2');
    await waitFor(() => expect(document.head.querySelector('script[type="application/ld+json"]')?.textContent).toContain('"Course"'));
    expect(await axeViolations(container)).toEqual([]);
  });

  it('lets anonymous visitors check knowledge-check answers in the browser and shows "video coming soon"', async () => {
    const { calls } = mockFetch({ ...anonymous, ...catalogRoutes });
    const { container } = renderWithApp(<AcademyLessonPage />, {
      route: '/learn/platform-getting-started/l2',
      path: '/learn/:slug/:lessonSlug',
    });
    expect(await screen.findByText('Video coming soon')).toBeInTheDocument();
    // GFM task list renders read-only checkboxes; tables and quotes render too.
    expect(screen.getByRole('checkbox', { name: 'Disclosure visible' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Disclosure visible' })).toBeDisabled();
    expect(screen.getByRole('table')).toBeInTheDocument();
    const group = screen.getByRole('group', { name: 'What is the proof?' });
    await userEvent.click(within(group).getByRole('radio', { name: 'A screenshot' }));
    await userEvent.click(within(group).getByRole('button', { name: 'Check answer' }));
    expect(await within(group).findByText('Not quite')).toBeInTheDocument();
    expect(calls.some((c) => c.method === 'POST' && c.path.includes('/checks/'))).toBe(false);
    expect(screen.getByText('Enrol for free to track progress, take the exam and earn your certificate')).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows a valid certificate on the verification page', async () => {
    mockFetch({
      ...anonymous,
      'GET /public/learning/certificates/cert-1': () =>
        json(200, {
          ...certificate,
          status: 'valid',
          isValid: true,
          holderName: 'Amina Khan',
          badgeDescription: 'Knows the platform.',
          criteria: 'Pass',
          issuerName: 'Optimize All Academy',
          seo: { ...seo, canonicalPath: '/verify/certificates/cert-1' },
          jsonLd: [],
        }),
    });
    const { container } = renderWithApp(<VerifyCertificatePage />, { route: '/verify/certificates/cert-1', path: '/verify/certificates/:id' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Amina Khan' })).toBeInTheDocument();
    expect(screen.getByTestId('verify-status')).toHaveTextContent('Valid certificate');
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('participant learning', () => {
  it('shows the dashboard with continue, certificates and recommendations', async () => {
    mockFetch({
      ...signedIn,
      'GET /me/learning': () =>
        json(200, {
          stats: { enrolled: 1, inProgress: 1, completed: 0, certificates: 1, lessonsCompleted: 1, minutesLearned: 10 },
          continue: {
            course: card,
            completedLessons: 1,
            lessonCount: 2,
            progressPercent: 50,
            nextLessonSlug: 'l2',
            nextLessonTitle: 'Submitting posts',
            examUnlocked: false,
            passed: false,
            bestScore: null,
            certificateId: null,
            enrolledAt: '2026-09-20T00:00:00Z',
            lastActivityAt: '2026-09-21T00:00:00Z',
          },
          inProgress: [],
          completed: [],
          recommended: [{ ...card, id: 'c2', slug: 'seo-basics', title: 'SEO basics', category: 'Seo' }],
          certificates: [certificate],
        }),
    });
    const { container } = renderWithApp(<LearningHomePage />, { route: '/app/learning', path: '/app/learning' });
    expect(await screen.findByText('Next: Submitting posts', { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Resume' })).toHaveAttribute('href', '/app/learning/courses/platform-getting-started/lessons/l2');
    expect(screen.getByRole('link', { name: 'SEO basics' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Certified Creator' })).toHaveAttribute('href', '/app/learning/certificates/cert-1');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('records knowledge-check answers on the server and marks a lesson complete', async () => {
    const { calls } = mockFetch({
      ...signedIn,
      'GET /me/learning/courses/platform-getting-started/lessons/l2': () =>
        json(200, { lesson, enrolled: true, completed: false, answers: [], progress: null }),
      'POST /me/learning/courses/platform-getting-started/lessons/l2/start': () => json(200, {}),
      'POST /me/learning/courses/platform-getting-started/lessons/l2/checks/0': () =>
        json(200, { index: 0, selected: [0], isCorrect: true, correct: [0], explanation: 'The link shows the live post.' }),
      'POST /me/learning/courses/platform-getting-started/lessons/l2/complete': () =>
        json(200, { examUnlocked: true, progressPercent: 100, completedLessons: ['l1', 'l2'] }),
    });
    renderWithApp(<LearningLessonPage />, {
      route: '/app/learning/courses/platform-getting-started/lessons/l2',
      path: '/app/learning/courses/:slug/lessons/:lessonSlug',
    });
    const group = await screen.findByRole('group', { name: 'What is the proof?' });
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/lessons/l2/start'))).toBe(true));
    await userEvent.click(within(group).getByRole('radio', { name: 'The public link' }));
    await userEvent.click(within(group).getByRole('button', { name: 'Check answer' }));
    expect(await within(group).findByText('Correct')).toBeInTheDocument();
    expect(calls.find((c) => c.path.endsWith('/checks/0'))?.body).toEqual({ selected: [0] });
    await userEvent.click(screen.getByRole('button', { name: 'Mark lesson complete' }));
    await waitFor(() => expect(calls.some((c) => c.path.endsWith('/lessons/l2/complete'))).toBe(true));
  });

  it('explains why the exam is locked', async () => {
    mockFetch({
      ...signedIn,
      'GET /me/learning/courses/platform-getting-started/exam': () =>
        json(200, {
          courseSlug: card.slug,
          courseTitle: card.title,
          rules: course.exam,
          enrolled: true,
          lessonsComplete: false,
          passed: false,
          certificateId: null,
          attemptsRemaining: 3,
          nextAttemptAt: null,
          activeAttemptId: null,
          canStart: false,
          blockedReason: 'learning.lessons_incomplete',
          attempts: [],
        }),
    });
    renderWithApp(<ExamOverviewPage />, { route: '/app/learning/courses/platform-getting-started/exam', path: '/app/learning/courses/:slug/exam' });
    expect(await screen.findByText('Complete every lesson to unlock the final assessment.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Start the assessment' })).not.toBeInTheDocument();
  });

  it('takes an exam: autosaves each answer, shows the server timer and submits after confirmation', async () => {
    const submitted: ExamAttempt = {
          ...attempt,
          status: 'Submitted',
          secondsRemaining: 0,
          questions: attempt.questions.map((q) => ({ ...q, selected: [0], review: { isCorrect: q.id === 'q1', correct: [0], explanation: `Because ${q.id}` } })),
          result: { score: 50, correctCount: 1, questionCount: 2, passed: false, passingScore: 80, certificateId: null, attemptsRemaining: 2, nextAttemptAt: null },
    };
    let done = false;
    const { calls } = mockFetch({
      ...signedIn,
      'GET /me/learning/attempts/a1': () => json(200, done ? submitted : attempt),
      'PUT /me/learning/attempts/a1/answers': () => json(200, attempt),
      'POST /me/learning/attempts/a1/submit': () => {
        done = true;
        return json(200, submitted);
      },
    });
    const { container } = renderWithApp(<ExamAttemptPage />, { route: '/app/learning/attempts/a1', path: '/app/learning/attempts/:attemptId' });
    expect(await screen.findByRole('timer', { name: 'Time left' })).toHaveTextContent(/1[45]:\d\d/);
    await userEvent.click(screen.getByRole('radio', { name: 'Public link' }));
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ questionId: 'q1', selected: [0] }));
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(screen.getByRole('button', { name: 'Next question' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Bought followers' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'Duplicates' }));
    await userEvent.click(screen.getByRole('button', { name: 'Submit answers' }));
    await userEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Submit' }));
    expect(await screen.findByTestId('exam-outcome')).toHaveTextContent('Not passed this time');
    expect(screen.getByText('Because q2')).toBeInTheDocument();
    const submit = calls.find((c) => c.method === 'POST' && c.path.endsWith('/submit'));
    expect(submit?.body).toEqual({ answers: [{ questionId: 'q1', selected: [0] }, { questionId: 'q2', selected: [0, 2] }] });
    expect(screen.getByRole('link', { name: /Retake/ })).toHaveAttribute('href', '/app/learning/courses/platform-getting-started/exam');
  });

  it('shows the certificate with PDF, LinkedIn and verification links', async () => {
    mockFetch({ ...signedIn, 'GET /me/learning/certificates/cert-1': () => json(200, certificate) });
    const { container } = renderWithApp(<LearningCertificatePage />, {
      route: '/app/learning/certificates/cert-1',
      path: '/app/learning/certificates/:id',
    });
    const add = await screen.findByTestId('linkedin-add');
    const url = new URL(add.getAttribute('href')!);
    expect(url.searchParams.get('startTask')).toBe('CERTIFICATION_NAME');
    expect(url.searchParams.get('certId')).toBe('OA-ABCD-2345');
    expect(screen.getByRole('link', { name: /Download PDF/ })).toHaveAttribute('href', certificate.links.pdfUrl);
    // Images come straight from the API's root-relative fields: always same-origin for the CSP (img-src 'self').
    const images = [...container.querySelectorAll('img')];
    expect(images.length).toBeGreaterThan(0);
    images.forEach((img) => expect(img.getAttribute('src')).toMatch(/^\/api\/v1\/public\/learning\//));
    expect(screen.getByRole('link', { name: 'Open the public verification page' })).toHaveAttribute('href', '/verify/certificates/cert-1');
    expect(await axeViolations(container)).toEqual([]);
  });
});
