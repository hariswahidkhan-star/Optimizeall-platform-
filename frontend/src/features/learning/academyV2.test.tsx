import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { json, mockFetch, problem, session, makeUser } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import {
  AcademyCoursePage,
  AcademyLessonPage,
  AcademyPathPage,
  AcademyPathsPage,
} from '@/features/public/learn/AcademyPages';
import { LearningCoursePage } from '@/features/participant/learning/CoursePages';
import type { CourseCard, CourseDetail, Lesson, LessonLecture, PathCard, PathDetail } from './api';
import { formatClock, formatReviewed } from './api';
import {
  authPathForEnrol,
  clearEnrolIntent,
  enrolReturnPath,
  pendingEnrolPath,
  pendingEnrolSlug,
  rememberEnrolIntent,
} from './enrolIntent';

const card: CourseCard = {
  id: 'c1',
  slug: 'ai-agents-engineering',
  title: 'AI Agents Engineering',
  subtitle: 'Build agents that work',
  category: 'Ai',
  level: 'Advanced',
  estimatedMinutes: 480,
  moduleCount: 1,
  lessonCount: 2,
  badgeName: 'Agent Engineer',
  skills: ['Agents'],
  isFeatured: false,
  isNew: true,
  badgeImageUrl: '/api/v1/public/learning/courses/ai-agents-engineering/badge.svg',
  publishedAt: '2026-09-20T00:00:00Z',
};
const seo = { title: 'AI Agents Engineering', description: 'Desc', canonicalPath: '/learn/ai-agents-engineering', imageUrl: null, noIndex: false };

const course: CourseDetail = {
  card,
  description: 'Build **agents**.',
  outcomes: ['Ship an agent'],
  prerequisites: [],
  badge: { name: 'Agent Engineer', description: 'Builds agents.', criteria: 'Pass.', imageUrl: card.badgeImageUrl },
  exam: { questionCount: 20, timeLimitMinutes: 30, maxAttemptsPerDay: 3, passingScore: 80 },
  modules: [
    {
      slug: 'm1',
      title: 'Foundations',
      summary: 'Basics.',
      lessons: [
        { slug: 'l1', title: 'What an agent is', type: 'Article', durationMinutes: 12, hasVideo: false, hasLecture: true, lectureMinutes: 8 },
        { slug: 'l2', title: 'Tools and loops', type: 'Article', durationMinutes: 14, hasVideo: true, hasLecture: true, lectureMinutes: 9 },
      ],
    },
  ],
  version: 2,
  updatedAt: '2026-09-20T00:00:00Z',
  seo,
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'Course', name: 'AI Agents Engineering' }],
  lastReviewed: '2026-09',
  tools: ['Claude', 'MCP'],
  lectureMinutes: 17,
  lectureCount: 2,
};

const chapters = [0, 1, 2, 3, 4].map((i) => ({
  index: i,
  title: `Chapter ${i + 1} title`,
  points: ['First point', 'Second point'],
  narration: `Narration of chapter ${i + 1}. You will learn something useful here.`,
  startSeconds: i * 60,
  seconds: 60,
}));

const soonLecture: LessonLecture = {
  title: 'What an agent is',
  targetMinutes: 8,
  totalSeconds: 300,
  produced: false,
  src: null,
  poster: null,
  captions: null,
  chapters,
  transcriptWords: 1040,
};

const lesson: Lesson = {
  courseSlug: card.slug,
  courseTitle: card.title,
  category: 'Ai',
  moduleSlug: 'm1',
  moduleTitle: 'Foundations',
  slug: 'l1',
  title: 'What an agent is',
  type: 'Article',
  durationMinutes: 12,
  body: '### Agents\n\nAn agent is a loop.',
  video: null,
  keyTakeaways: ['Agents loop.'],
  knowledgeCheck: [],
  activity: null,
  previous: null,
  next: { slug: 'l2', title: 'Tools and loops' },
  position: 1,
  lessonCount: 2,
  seo,
  jsonLd: [],
  lecture: soonLecture,
  lastReviewed: '2026-09',
};

const pathCard: PathCard = {
  slug: 'ai-engineer',
  title: 'AI Engineer',
  subtitle: 'Build production AI',
  level: 'Advanced',
  courseCount: 2,
  lessonCount: 20,
  totalMinutes: 900,
  categories: ['Ai'],
  badges: [
    { courseSlug: 'ai-agents-engineering', courseTitle: 'AI Agents Engineering', badgeName: 'Agent Engineer', imageUrl: card.badgeImageUrl },
    { courseSlug: 'mastering-claude', courseTitle: 'Mastering Claude', badgeName: 'Claude Pro', imageUrl: '/api/v1/public/learning/courses/mastering-claude/badge.svg' },
  ],
};

const path: PathDetail = {
  card: pathCard,
  description: 'From prompts to **production**.',
  outcomes: ['Ship agents'],
  audience: ['Developers'],
  courses: [
    { position: 1, course: card },
    { position: 2, course: { ...card, id: 'c2', slug: 'mastering-claude', title: 'Mastering Claude', badgeName: 'Claude Pro' } },
  ],
  seo: { ...seo, title: 'AI Engineer learning path', canonicalPath: '/learn/paths/ai-engineer' },
  jsonLd: [{ '@context': 'https://schema.org', '@type': 'ItemList', name: 'AI Engineer' }],
};

const anonymous = { 'POST /auth/refresh': () => problem(401, 'auth.refresh_invalid', 'No session') };
const signedIn = { 'POST /auth/refresh': () => json(200, session()) };
const publicRoutes = {
  'GET /public/site': () => problem(404, 'http_404', 'Not found'),
  'GET /public/learning/courses/ai-agents-engineering': () => json(200, course),
  'GET /public/learning/courses/ai-agents-engineering/lessons/l1': () => json(200, lesson),
  'GET /public/learning/paths': () => json(200, { paths: [pathCard], seo: path.seo, jsonLd: [] }),
  'GET /public/learning/paths/ai-engineer': () => json(200, path),
};

afterEach(() => clearEnrolIntent());

describe('enrol intent (return path + remembered course)', () => {
  it('builds a same-origin return path and remembers the course for a week', () => {
    expect(enrolReturnPath('ai-agents-engineering')).toBe('/learn/ai-agents-engineering?enrol=1');
    expect(authPathForEnrol('ai-agents-engineering', 'register')).toBe('/register?next=%2Flearn%2Fai-agents-engineering%3Fenrol%3D1');
    const now = Date.now();
    rememberEnrolIntent('ai-agents-engineering', now);
    expect(pendingEnrolSlug(now)).toBe('ai-agents-engineering');
    expect(pendingEnrolPath(now)).toBe('/learn/ai-agents-engineering?enrol=1');
    expect(pendingEnrolSlug(now + 8 * 24 * 3600 * 1000)).toBeNull();
    clearEnrolIntent();
    expect(pendingEnrolPath()).toBeNull();
  });

  it('never stores or returns anything that is not a course slug', () => {
    rememberEnrolIntent('//evil.example.com');
    expect(pendingEnrolPath()).toBeNull();
    window.localStorage.setItem('oa.learn.enrolIntent', JSON.stringify({ slug: '../../admin', at: Date.now() }));
    expect(pendingEnrolPath()).toBeNull();
    window.localStorage.setItem('oa.learn.enrolIntent', '{broken');
    expect(pendingEnrolPath()).toBeNull();
  });

  it('formats review months and chapter times', () => {
    expect(formatReviewed('2026-09')).toBe('Sep 2026');
    expect(formatReviewed('2026-13')).toBeNull();
    expect(formatReviewed(null)).toBeNull();
    expect(formatClock(65)).toBe('1:05');
    expect(formatClock(3725)).toBe('1:02:05');
  });
});

describe('course page v2 and direct enrol', () => {
  it('shows Updated, tools and lecture minutes, and sends a signed-out visitor to register with a return path', async () => {
    mockFetch({ ...anonymous, ...publicRoutes });
    const { container, router } = renderWithApp(<AcademyCoursePage />, {
      route: '/learn/ai-agents-engineering',
      path: '/learn/:slug',
      routes: [{ path: '/register', element: <p>Register page</p> }],
    });
    expect(await screen.findByRole('heading', { level: 1, name: card.title })).toBeInTheDocument();
    expect(screen.getByText('Updated Sep 2026', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('17 min of video lectures', { exact: false })).toBeInTheDocument();
    const tools = screen.getByRole('list', { name: 'Tools you’ll use' });
    expect(within(tools).getByText('MCP')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Preview lesson 1/ })).toHaveAttribute('href', '/learn/ai-agents-engineering/l1');
    expect(await axeViolations(container)).toEqual([]);
    await userEvent.click(screen.getAllByRole('button', { name: /Enrol for free — start learning/ })[0]!);
    await waitFor(() => expect(router.state.location.pathname).toBe('/register'));
    expect(router.state.location.search).toBe('?next=%2Flearn%2Fai-agents-engineering%3Fenrol%3D1');
    expect(pendingEnrolSlug()).toBe('ai-agents-engineering');
  });

  it('back on the course with ?enrol=1 and a session, enrols once and opens the first unfinished lesson', async () => {
    rememberEnrolIntent('ai-agents-engineering');
    const { calls } = mockFetch({
      ...signedIn,
      ...publicRoutes,
      'POST /me/learning/courses/ai-agents-engineering/enrol': () =>
        json(201, { course, progress: { resumeLessonSlug: 'l1', completedLessons: [], progressPercent: 0 } }),
    });
    const { router } = renderWithApp(<AcademyCoursePage />, {
      route: '/learn/ai-agents-engineering?enrol=1',
      path: '/learn/:slug',
      routes: [{ path: '/app/learning/courses/:slug/lessons/:lessonSlug', element: <p>Portal lesson</p> }],
    });
    await waitFor(() => expect(router.state.location.pathname).toBe('/app/learning/courses/ai-agents-engineering/lessons/l1'));
    expect(calls.filter((c) => c.method === 'POST' && c.path.endsWith('/enrol'))).toHaveLength(1);
    expect(pendingEnrolSlug()).toBeNull();
  });

  it('a signed-in learner enrols from the button and lands on lesson 1', async () => {
    mockFetch({
      ...signedIn,
      ...publicRoutes,
      'POST /me/learning/courses/ai-agents-engineering/enrol': () =>
        json(200, { course, progress: { resumeLessonSlug: 'l2', completedLessons: ['l1'], progressPercent: 50 } }),
    });
    const { router } = renderWithApp(<AcademyCoursePage />, {
      route: '/learn/ai-agents-engineering',
      path: '/learn/:slug',
      routes: [{ path: '/app/learning/courses/:slug/lessons/:lessonSlug', element: <p>Portal lesson</p> }],
    });
    await userEvent.click((await screen.findAllByRole('button', { name: /Enrol for free — start learning/ }))[0]!);
    await waitFor(() => expect(router.state.location.pathname).toBe('/app/learning/courses/ai-agents-engineering/lessons/l2'));
  });

  it('staff accounts are told enrolment is for learners and are not enrolled', async () => {
    const { calls } = mockFetch({
      'POST /auth/refresh': () => json(200, session(makeUser({ roles: ['Admin'], permissions: ['campaigns.view'] }))),
      ...publicRoutes,
    });
    renderWithApp(<AcademyCoursePage />, { route: '/learn/ai-agents-engineering?enrol=1', path: '/learn/:slug' });
    expect(await screen.findByText(/staff account/)).toBeInTheDocument();
    expect(calls.some((c) => c.path.endsWith('/enrol'))).toBe(false);
  });

  it('the portal course page completes a pending ?enrol=1 too', async () => {
    const { calls } = mockFetch({
      ...signedIn,
      'GET /me/learning/courses/ai-agents-engineering': () => json(200, { course, progress: null }),
      'POST /me/learning/courses/ai-agents-engineering/enrol': () =>
        json(201, { course, progress: { resumeLessonSlug: 'l1', completedLessons: [], progressPercent: 0 } }),
    });
    const { router } = renderWithApp(<LearningCoursePage />, {
      route: '/app/learning/courses/ai-agents-engineering?enrol=1',
      path: '/app/learning/courses/:slug',
      routes: [{ path: '/app/learning/courses/:slug/lessons/:lessonSlug', element: <p>Portal lesson</p> }],
    });
    await waitFor(() => expect(router.state.location.pathname).toBe('/app/learning/courses/ai-agents-engineering/lessons/l1'));
    expect(calls.filter((c) => c.path.endsWith('/enrol'))).toHaveLength(1);
  });
});

describe('video lecture', () => {
  it('shows "coming soon" with chapters, a slide preview and the full transcript on demand', async () => {
    mockFetch({ ...anonymous, ...publicRoutes });
    const { container } = renderWithApp(<AcademyLessonPage />, { route: '/learn/ai-agents-engineering/l1', path: '/learn/:slug/:lessonSlug' });
    const section = await screen.findByRole('region', { name: 'What an agent is' });
    expect(within(section).getByText('Coming soon')).toBeInTheDocument();
    expect(within(section).getAllByRole('button', { name: /^\d+:\d\d\s*Chapter \d title$/ })).toHaveLength(5);
    // Selecting a chapter previews its slide.
    await userEvent.click(within(section).getByRole('button', { name: /^\d+:\d\d\s*Chapter 3 title$/ }));
    expect(within(section).getByText('Chapter 3 of 5')).toBeInTheDocument();
    // Transcript: collapsed, then opened.
    const toggle = within(section).getByRole('button', { name: /^Read the transcript\s*·/ });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    const transcript = screen.getByRole('region', { name: 'Lecture transcript' });
    expect(within(transcript).getByText('Narration of chapter 4. You will learn something useful here.')).toBeVisible();
    expect(await axeViolations(container)).toEqual([]);
  });

  it('plays a produced lecture with captions, chapter seek and playback speed', async () => {
    const produced: Lesson = {
      ...lesson,
      lecture: { ...soonLecture, produced: true, src: 'https://cdn.example.com/l.mp4', poster: 'https://cdn.example.com/p.jpg', captions: 'https://cdn.example.com/c.vtt' },
    };
    mockFetch({ ...anonymous, ...publicRoutes, 'GET /public/learning/courses/ai-agents-engineering/lessons/l1': () => json(200, produced) });
    const { container } = renderWithApp(<AcademyLessonPage />, { route: '/learn/ai-agents-engineering/l1', path: '/learn/:slug/:lessonSlug' });
    const section = await screen.findByRole('region', { name: 'What an agent is' });
    const video = container.querySelector('video')!;
    expect(video).toHaveAttribute('poster', 'https://cdn.example.com/p.jpg');
    expect(video.querySelector('track[kind="captions"]')).toHaveAttribute('src', 'https://cdn.example.com/c.vtt');
    const speed = within(section).getByRole('radiogroup', { name: 'Playback speed' });
    await userEvent.click(within(speed).getByRole('radio', { name: '1.5×' }));
    expect(within(speed).getByRole('radio', { name: '1.5×' })).toHaveAttribute('aria-checked', 'true');
    expect(video.playbackRate).toBe(1.5);
    await userEvent.click(within(section).getByRole('button', { name: /Chapter 2 title/ }));
    expect(video.currentTime).toBe(60);
    expect(within(section).getByRole('button', { name: /Chapter 2 title/ })).toHaveAttribute('aria-current', 'step');
  });
});

describe('learning paths', () => {
  it('lists paths and shows a path as an ordered course timeline with badges', async () => {
    mockFetch({ ...anonymous, ...publicRoutes });
    const list = renderWithApp(<AcademyPathsPage />, { route: '/learn/paths', path: '/learn/paths' });
    expect(await screen.findByRole('link', { name: 'AI Engineer' })).toHaveAttribute('href', '/learn/paths/ai-engineer');
    expect(await axeViolations(list.container)).toEqual([]);
    list.unmount();

    const { container } = renderWithApp(<AcademyPathPage />, { route: '/learn/paths/ai-engineer', path: '/learn/paths/:pathSlug' });
    expect(await screen.findByRole('heading', { level: 1, name: 'AI Engineer' })).toBeInTheDocument();
    const steps = screen.getAllByRole('heading', { level: 3 });
    expect(steps.map((h) => h.textContent)).toEqual(['AI Agents Engineering', 'Mastering Claude']);
    expect(screen.getByRole('link', { name: 'Mastering Claude' })).toHaveAttribute('href', '/learn/mastering-claude');
    expect(screen.getByRole('link', { name: /Start with AI Agents Engineering/ })).toHaveAttribute('href', '/learn/ai-agents-engineering');
    await waitFor(() => expect(document.head.querySelector('script[type="application/ld+json"]')?.textContent).toContain('"ItemList"'));
    expect(await axeViolations(container)).toEqual([]);
  });

  it('shows a signed-in learner’s progress and next course', async () => {
    mockFetch({
      ...signedIn,
      ...publicRoutes,
      'GET /me/learning/paths/ai-engineer': () =>
        json(200, {
          path,
          progress: {
            slug: 'ai-engineer',
            completedCourses: 1,
            courseCount: 2,
            progressPercent: 60,
            nextCourseSlug: 'mastering-claude',
            started: true,
            courses: [
              { slug: 'ai-agents-engineering', enrolled: true, progressPercent: 100, passed: true, certificateId: 'x' },
              { slug: 'mastering-claude', enrolled: true, progressPercent: 20, passed: false, certificateId: null },
            ],
          },
        }),
    });
    renderWithApp(<AcademyPathPage />, { route: '/learn/paths/ai-engineer', path: '/learn/paths/:pathSlug' });
    expect(await screen.findByRole('link', { name: /Continue: Mastering Claude/ })).toBeInTheDocument();
    expect(screen.getByText('1 of 2 earned')).toBeInTheDocument();
    expect(screen.getByText('Up next')).toBeInTheDocument();
  });
});
