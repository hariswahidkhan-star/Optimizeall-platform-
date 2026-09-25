import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api/client';

/** Learning module (docs/LEARNING.md): the free academy, my learning, exams and certificates. Mirrors LearningDtos.cs. */

export type CourseCategory = 'Sales' | 'Marketing' | 'Seo' | 'Ai' | 'Business' | 'Design' | 'Data' | 'Platform';
export type CourseLevel = 'Beginner' | 'Intermediate' | 'Advanced';
export type LessonType = 'Article' | 'Video';
export type QuestionType = 'Single' | 'Multiple';
export type ExamAttemptStatus = 'InProgress' | 'Submitted' | 'Expired';

export const CATEGORY_LABELS: Record<CourseCategory, string> = {
  Sales: 'Sales',
  Marketing: 'Marketing',
  Seo: 'SEO',
  Ai: 'AI',
  Business: 'Business',
  Design: 'Design',
  Data: 'Data & analytics',
  Platform: 'Optimize All platform',
};

export const CATEGORIES = Object.keys(CATEGORY_LABELS) as CourseCategory[];
export const LEVELS: CourseLevel[] = ['Beginner', 'Intermediate', 'Advanced'];

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface CourseCard {
  id: string;
  slug: string;
  title: string;
  subtitle: string;
  category: CourseCategory;
  level: CourseLevel;
  estimatedMinutes: number;
  moduleCount: number;
  lessonCount: number;
  badgeName: string;
  skills: string[];
  isFeatured: boolean;
  isNew: boolean;
  badgeImageUrl: string;
  publishedAt: string | null;
}

export interface CategorySummary {
  category: CourseCategory;
  label: string;
  courseCount: number;
}

export interface LearningSeo {
  title: string;
  description: string;
  canonicalPath: string;
  imageUrl: string | null;
  noIndex: boolean;
}

export type JsonLd = Record<string, unknown>;

export interface LessonSummary {
  slug: string;
  title: string;
  type: LessonType;
  durationMinutes: number;
  hasVideo: boolean;
  /** Pack v2: the lesson has a video lecture (produced or coming soon). */
  hasLecture: boolean;
  lectureMinutes: number;
}

export interface CourseModule {
  slug: string;
  title: string;
  summary: string;
  lessons: LessonSummary[];
}

export interface ExamInfo {
  questionCount: number;
  timeLimitMinutes: number;
  maxAttemptsPerDay: number;
  passingScore: number;
}

export interface CourseDetail {
  card: CourseCard;
  description: string;
  outcomes: string[];
  prerequisites: { slug: string; title: string }[];
  badge: { name: string; description: string; criteria: string; imageUrl: string };
  exam: ExamInfo;
  modules: CourseModule[];
  version: number;
  updatedAt: string;
  seo: LearningSeo;
  jsonLd: JsonLd[];
  /** Pack v2: review month ("2026-09"), shown as "Updated Sep 2026". */
  lastReviewed: string | null;
  tools: string[];
  lectureMinutes: number;
  lectureCount: number;
}

export interface LectureChapter {
  index: number;
  title: string;
  points: string[];
  narration: string;
  startSeconds: number;
  seconds: number;
}

/** A lesson's video lecture: produced (src) or "coming soon" with chapters and the full transcript. */
export interface LessonLecture {
  title: string;
  targetMinutes: number;
  totalSeconds: number;
  produced: boolean;
  src: string | null;
  poster: string | null;
  captions: string | null;
  chapters: LectureChapter[];
  transcriptWords: number;
  /** YouTube-hosted lecture: its video id and the privacy-enhanced embed URL (youtube-nocookie.com). */
  youTubeId: string | null;
  embedUrl: string | null;
  publishedAt: string | null;
}

export interface KnowledgeCheck {
  index: number;
  question: string;
  options: string[];
  correct: number[];
  explanation: string;
  multiple: boolean;
}

export interface Lesson {
  courseSlug: string;
  courseTitle: string;
  category: CourseCategory;
  moduleSlug: string;
  moduleTitle: string;
  slug: string;
  title: string;
  type: LessonType;
  durationMinutes: number;
  body: string;
  video: { src: string | null; poster: string | null; captions: string | null; transcript: string } | null;
  keyTakeaways: string[];
  knowledgeCheck: KnowledgeCheck[];
  activity: string | null;
  previous: { slug: string; title: string } | null;
  next: { slug: string; title: string } | null;
  position: number;
  lessonCount: number;
  seo: LearningSeo;
  jsonLd: JsonLd[];
  lecture: LessonLecture | null;
  lastReviewed: string | null;
}

export interface CourseProgress {
  enrolmentId: string;
  enrolledAt: string;
  completedLessons: string[];
  resumeLessonSlug: string | null;
  completedLessonCount: number;
  lessonCount: number;
  progressPercent: number;
  examUnlocked: boolean;
  passed: boolean;
  bestScore: number | null;
  certificateId: string | null;
  lastActivityAt: string | null;
}

export interface MyCourseCard {
  course: CourseCard;
  enrolled: boolean;
  progressPercent: number;
  passed: boolean;
  certificateId: string | null;
}

export interface MyCourse {
  course: CourseDetail;
  progress: CourseProgress | null;
}

export interface KnowledgeCheckResult {
  index: number;
  selected: number[];
  isCorrect: boolean;
  correct: number[];
  explanation: string;
}

export interface MyLesson {
  lesson: Lesson;
  enrolled: boolean;
  completed: boolean;
  answers: KnowledgeCheckResult[];
  progress: CourseProgress | null;
}

export interface EnrolmentCard {
  course: CourseCard;
  completedLessons: number;
  lessonCount: number;
  progressPercent: number;
  nextLessonSlug: string | null;
  nextLessonTitle: string | null;
  examUnlocked: boolean;
  passed: boolean;
  bestScore: number | null;
  certificateId: string | null;
  enrolledAt: string;
  lastActivityAt: string | null;
}

export interface CertificateLinks {
  verificationUrl: string;
  pdfUrl: string;
  imageUrl: string;
  badgeImageUrl: string;
  openBadgeAssertionUrl: string;
  linkedInAddToProfileUrl: string;
  linkedInShareUrl: string;
}

export interface MyCertificate {
  id: string;
  verificationCode: string;
  courseSlug: string;
  courseTitle: string;
  badgeName: string;
  skills: string[];
  score: number | null;
  issuedAt: string;
  revoked: boolean;
  revokedAt: string | null;
  links: CertificateLinks;
}

export interface LearningDashboard {
  stats: { enrolled: number; inProgress: number; completed: number; certificates: number; lessonsCompleted: number; minutesLearned: number };
  continue: EnrolmentCard | null;
  inProgress: EnrolmentCard[];
  completed: EnrolmentCard[];
  recommended: CourseCard[];
  certificates: MyCertificate[];
}

export interface AttemptSummary {
  id: string;
  status: ExamAttemptStatus;
  startedAt: string;
  submittedAt: string | null;
  score: number | null;
  passed: boolean | null;
  questionCount: number;
}

export interface ExamOverview {
  courseSlug: string;
  courseTitle: string;
  rules: ExamInfo;
  enrolled: boolean;
  lessonsComplete: boolean;
  passed: boolean;
  certificateId: string | null;
  attemptsRemaining: number;
  nextAttemptAt: string | null;
  activeAttemptId: string | null;
  canStart: boolean;
  blockedReason: string | null;
  attempts: AttemptSummary[];
}

export interface AttemptQuestion {
  id: string;
  number: number;
  type: QuestionType;
  question: string;
  options: string[];
  selected: number[];
  review: { isCorrect: boolean; correct: number[]; explanation: string } | null;
}

export interface ExamAttempt {
  id: string;
  courseSlug: string;
  courseTitle: string;
  status: ExamAttemptStatus;
  startedAt: string;
  deadlineAt: string;
  serverNow: string;
  secondsRemaining: number;
  questionCount: number;
  passingScore: number;
  questions: AttemptQuestion[];
  result: {
    score: number;
    correctCount: number;
    questionCount: number;
    passed: boolean;
    passingScore: number;
    certificateId: string | null;
    attemptsRemaining: number;
    nextAttemptAt: string | null;
  } | null;
}

export interface CertificateVerification {
  id: string;
  verificationCode: string;
  status: 'valid' | 'revoked';
  isValid: boolean;
  holderName: string;
  courseSlug: string;
  courseTitle: string;
  badgeName: string;
  badgeDescription: string | null;
  criteria: string | null;
  skills: string[];
  issuedAt: string;
  revokedAt: string | null;
  issuerName: string;
  links: CertificateLinks;
  seo: LearningSeo;
  jsonLd: JsonLd[];
}

// ---------------------------------------------------------------- learning paths

export interface PathBadge {
  courseSlug: string;
  courseTitle: string;
  badgeName: string;
  imageUrl: string;
}

export interface PathCard {
  slug: string;
  title: string;
  subtitle: string;
  level: CourseLevel;
  courseCount: number;
  lessonCount: number;
  totalMinutes: number;
  categories: CourseCategory[];
  badges: PathBadge[];
}

export interface PathDetail {
  card: PathCard;
  description: string;
  outcomes: string[];
  audience: string[];
  courses: { position: number; course: CourseCard }[];
  seo: LearningSeo;
  jsonLd: JsonLd[];
}

export interface PathsIndex {
  paths: PathCard[];
  seo: LearningSeo;
  jsonLd: JsonLd[];
}

export interface PathCourseProgress {
  slug: string;
  enrolled: boolean;
  progressPercent: number;
  passed: boolean;
  certificateId: string | null;
}

export interface PathProgress {
  slug: string;
  completedCourses: number;
  courseCount: number;
  progressPercent: number;
  nextCourseSlug: string | null;
  started: boolean;
  courses: PathCourseProgress[];
}

export interface CatalogFilters {
  category?: CourseCategory | '';
  level?: CourseLevel | '';
  maxMinutes?: number | '';
  search?: string;
  sort?: '' | 'title' | 'newest' | 'duration' | 'level';
  page?: number;
  pageSize?: number;
}

export const learningKeys = {
  all: ['learning'] as const,
  publicCatalog: (f: CatalogFilters) => ['learning', 'public', 'catalog', f] as const,
  categories: ['learning', 'public', 'categories'] as const,
  publicCourse: (slug: string) => ['learning', 'public', 'course', slug] as const,
  publicLesson: (slug: string, lesson: string) => ['learning', 'public', 'lesson', slug, lesson] as const,
  verify: (id: string) => ['learning', 'verify', id] as const,
  paths: ['learning', 'public', 'paths'] as const,
  path: (slug: string) => ['learning', 'public', 'paths', slug] as const,
  myPaths: ['me', 'learning', 'paths'] as const,
  myPath: (slug: string) => ['me', 'learning', 'paths', slug] as const,
  me: ['me', 'learning'] as const,
  myCatalog: (f: CatalogFilters) => ['me', 'learning', 'catalog', f] as const,
  myCourse: (slug: string) => ['me', 'learning', 'course', slug] as const,
  myLesson: (slug: string, lesson: string) => ['me', 'learning', 'lesson', slug, lesson] as const,
  exam: (slug: string) => ['me', 'learning', 'exam', slug] as const,
  attempt: (id: string) => ['me', 'learning', 'attempt', id] as const,
  certificates: ['me', 'learning', 'certificates'] as const,
  certificate: (id: string) => ['me', 'learning', 'certificates', id] as const,
};

function catalogQuery(f: CatalogFilters) {
  return {
    category: f.category || undefined,
    level: f.level || undefined,
    maxMinutes: f.maxMinutes || undefined,
    search: f.search?.trim() || undefined,
    sort: f.sort || undefined,
    desc: f.sort === 'title' || f.sort === 'duration' || f.sort === 'level' ? false : undefined,
    page: f.page ?? 1,
    pageSize: f.pageSize ?? 24,
  };
}

// ---------------------------------------------------------------- public

export function usePublicCatalog(filters: CatalogFilters) {
  return useQuery({
    queryKey: learningKeys.publicCatalog(filters),
    queryFn: () => api.get<PagedResult<CourseCard>>('/public/learning/courses', { query: catalogQuery(filters) }),
    placeholderData: keepPreviousData,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: learningKeys.categories,
    queryFn: () => api.get<CategorySummary[]>('/public/learning/categories'),
    staleTime: 5 * 60_000,
  });
}

export function usePublicCourse(slug: string) {
  return useQuery({
    queryKey: learningKeys.publicCourse(slug),
    queryFn: () => api.get<CourseDetail>(`/public/learning/courses/${encodeURIComponent(slug)}`),
  });
}

export function usePublicLesson(slug: string, lesson: string) {
  return useQuery({
    queryKey: learningKeys.publicLesson(slug, lesson),
    queryFn: () =>
      api.get<Lesson>(`/public/learning/courses/${encodeURIComponent(slug)}/lessons/${encodeURIComponent(lesson)}`),
  });
}

export function useCertificateVerification(id: string) {
  return useQuery({
    queryKey: learningKeys.verify(id),
    queryFn: () => api.get<CertificateVerification>(`/public/learning/certificates/${encodeURIComponent(id)}`),
    retry: false,
  });
}

export function usePaths() {
  return useQuery({
    queryKey: learningKeys.paths,
    queryFn: () => api.get<PathsIndex>('/public/learning/paths'),
    staleTime: 5 * 60_000,
  });
}

export function usePath(slug: string) {
  return useQuery({
    queryKey: learningKeys.path(slug),
    queryFn: () => api.get<PathDetail>(`/public/learning/paths/${encodeURIComponent(slug)}`),
  });
}

/** The signed-in learner's progress on every path (participants only; pass enabled=false otherwise). */
export function useMyPaths(enabled = true) {
  return useQuery({
    queryKey: learningKeys.myPaths,
    queryFn: () => api.get<{ card: PathCard; progress: PathProgress }[]>('/me/learning/paths'),
    enabled,
  });
}

export function useMyPath(slug: string, enabled = true) {
  return useQuery({
    queryKey: learningKeys.myPath(slug),
    queryFn: () => api.get<{ path: PathDetail; progress: PathProgress }>(`/me/learning/paths/${encodeURIComponent(slug)}`),
    enabled,
    retry: false,
  });
}

// ---------------------------------------------------------------- my learning

const me = (slug: string) => `/me/learning/courses/${encodeURIComponent(slug)}`;

export function useLearningDashboard(enabled = true) {
  return useQuery({
    queryKey: learningKeys.me,
    queryFn: () => api.get<LearningDashboard>('/me/learning'),
    enabled,
  });
}

export function useMyCatalog(filters: CatalogFilters) {
  return useQuery({
    queryKey: learningKeys.myCatalog(filters),
    queryFn: () => api.get<PagedResult<MyCourseCard>>('/me/learning/courses', { query: catalogQuery(filters) }),
    placeholderData: keepPreviousData,
  });
}

export function useMyCourse(slug: string) {
  return useQuery({ queryKey: learningKeys.myCourse(slug), queryFn: () => api.get<MyCourse>(me(slug)) });
}

export function useMyLesson(slug: string, lesson: string) {
  return useQuery({
    queryKey: learningKeys.myLesson(slug, lesson),
    queryFn: () => api.get<MyLesson>(`${me(slug)}/lessons/${encodeURIComponent(lesson)}`),
  });
}

export function useEnrol(slug: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<MyCourse>(`${me(slug)}/enrol`),
    onSuccess: (data) => {
      qc.setQueryData(learningKeys.myCourse(slug), data);
      void qc.invalidateQueries({ queryKey: learningKeys.me });
    },
  });
}

export function useLessonProgress(slug: string, lesson: string) {
  const qc = useQueryClient();
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: learningKeys.myCourse(slug) });
    void qc.invalidateQueries({ queryKey: learningKeys.myLesson(slug, lesson) });
    void qc.invalidateQueries({ queryKey: learningKeys.exam(slug) });
    void qc.invalidateQueries({ queryKey: ['me', 'learning', 'catalog'] });
    void qc.invalidateQueries({ queryKey: learningKeys.me, exact: true });
  };
  const start = useMutation({
    mutationFn: () => api.post<CourseProgress>(`${me(slug)}/lessons/${encodeURIComponent(lesson)}/start`),
  });
  const complete = useMutation({
    mutationFn: () => api.post<CourseProgress>(`${me(slug)}/lessons/${encodeURIComponent(lesson)}/complete`),
    onSuccess: refresh,
  });
  return { start, complete };
}

export function useAnswerCheck(slug: string, lesson: string) {
  return useMutation({
    mutationFn: ({ index, selected }: { index: number; selected: number[] }) =>
      api.post<KnowledgeCheckResult>(`${me(slug)}/lessons/${encodeURIComponent(lesson)}/checks/${index}`, { selected }),
  });
}

export function useExamOverview(slug: string) {
  return useQuery({ queryKey: learningKeys.exam(slug), queryFn: () => api.get<ExamOverview>(`${me(slug)}/exam`) });
}

export function useStartExam(slug: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<ExamAttempt>(`${me(slug)}/exam/attempts`),
    onSuccess: (attempt) => {
      qc.setQueryData(learningKeys.attempt(attempt.id), attempt);
      void qc.invalidateQueries({ queryKey: learningKeys.exam(slug) });
    },
  });
}

export function useAttempt(id: string) {
  return useQuery({
    queryKey: learningKeys.attempt(id),
    queryFn: () => api.get<ExamAttempt>(`/me/learning/attempts/${encodeURIComponent(id)}`),
    refetchOnWindowFocus: false,
  });
}

export function useSaveAnswer(id: string) {
  return useMutation({
    mutationFn: (input: { questionId: string; selected: number[] }) =>
      api.put<ExamAttempt>(`/me/learning/attempts/${encodeURIComponent(id)}/answers`, input),
  });
}

export function useSubmitAttempt(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (answers: { questionId: string; selected: number[] }[]) =>
      api.post<ExamAttempt>(`/me/learning/attempts/${encodeURIComponent(id)}/submit`, { answers }),
    onSuccess: (attempt) => {
      qc.setQueryData(learningKeys.attempt(id), attempt);
      void qc.invalidateQueries({ queryKey: ['me', 'learning'] });
    },
  });
}

export function useMyCertificates() {
  return useQuery({ queryKey: learningKeys.certificates, queryFn: () => api.get<MyCertificate[]>('/me/learning/certificates') });
}

export function useMyCertificate(id: string) {
  return useQuery({
    queryKey: learningKeys.certificate(id),
    queryFn: () => api.get<MyCertificate>(`/me/learning/certificates/${encodeURIComponent(id)}`),
  });
}

// ---------------------------------------------------------------- helpers

/** Page keywords for partner slots and topic matching: the course's skills plus its category label. */
export function courseKeywords(skills: readonly string[], category: CourseCategory): string[] {
  return [...skills, CATEGORY_LABELS[category]];
}

/** "Sep 2026" from a pack's review month ("2026-09"); null when absent or malformed. */
export function formatReviewed(lastReviewed: string | null | undefined): string | null {
  const m = /^(\d{4})-(\d{2})$/.exec(lastReviewed ?? '');
  if (!m) return null;
  const month = Number(m[2]);
  if (month < 1 || month > 12) return null;
  const names = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `${names[month - 1]} ${m[1]}`;
}

/** 0:00 / 1:05:09 style time for the lecture chapters. */
export function formatClock(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = String(s % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${sec}` : `${m}:${sec}`;
}

/** Hours for path and hub totals ("12 h", "1.5 h"). */
export function formatHours(minutes: number): string {
  const h = minutes / 60;
  if (h < 1) return `${minutes} min`;
  return `${h >= 10 ? Math.round(h) : Math.round(h * 2) / 2} h`;
}

export function formatMinutes(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h} h` : `${h} h ${m} min`;
}

export const BLOCKED_REASONS: Record<string, string> = {
  'learning.not_enrolled': 'Enrol in the course to take the final assessment.',
  'learning.lessons_incomplete': 'Complete every lesson to unlock the final assessment.',
  'learning.already_certified': 'You passed this course and hold its certificate.',
  'learning.attempt_in_progress': 'You have an attempt in progress.',
  'learning.attempt_limit': 'You have used all attempts for the last 24 hours.',
};
