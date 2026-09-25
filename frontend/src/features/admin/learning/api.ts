import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { CourseCategory, CourseLevel, PagedResult } from '@/features/learning/api';

export type CourseStatus = 'Draft' | 'Published';
export type CourseSource = 'Pack' | 'Admin';

export interface AdminCourseRow {
  id: string;
  slug: string;
  title: string;
  category: CourseCategory;
  level: CourseLevel;
  status: CourseStatus;
  origin: CourseSource;
  isFeatured: boolean;
  sortOrder: number;
  lessonCount: number;
  publishedVersionNumber: number | null;
  latestVersionNumber: number | null;
  packUpdateAvailable: boolean;
  enrolments: number;
  completions: number;
  completionRate: number;
  attempts: number;
  passRate: number;
  averageScore: number | null;
  certificates: number;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface CourseVersion {
  id: string;
  number: number;
  source: CourseSource;
  packVersion: number | null;
  basedOnVersionId: string | null;
  note: string | null;
  createdAt: string;
  createdBy: string | null;
  isPublished: boolean;
  isLatest: boolean;
}

export interface AdminCourseDetail {
  summary: AdminCourseRow;
  versions: CourseVersion[];
  packFile: string | null;
}

export interface PackIssue {
  path: string;
  message: string;
}

export interface ValidationReport {
  valid: boolean;
  issues: PackIssue[];
  moduleCount: number;
  lessonCount: number;
  poolSize: number;
  questionCount: number;
}

export interface QuestionStat {
  questionId: string;
  module: string;
  difficulty: 'Easy' | 'Medium' | 'Hard';
  type: 'Single' | 'Multiple';
  question: string;
  answered: number;
  correct: number;
  percentCorrect: number | null;
  percentCorrectPassers: number | null;
  percentCorrectOthers: number | null;
  discrimination: number | null;
}

export interface LearnerRow {
  userId: string;
  displayName: string;
  email: string;
  enrolledAt: string;
  completedLessons: number;
  lessonCount: number;
  progressPercent: number;
  attempts: number;
  bestScore: number | null;
  passed: boolean;
  certificateId: string | null;
  certificateRevoked: boolean;
  lastActivityAt: string | null;
}

export interface AdminCertificate {
  id: string;
  verificationCode: string;
  userId: string;
  holderName: string;
  email: string;
  courseId: string;
  courseTitle: string;
  score: number | null;
  issuedAt: string;
  manual: boolean;
  revokedAt: string | null;
  revocationReason: string | null;
  concurrencyStamp: string;
}

/** The course pack document (docs/LEARNING.md). */
export interface PackCheck {
  question: string;
  options: string[];
  correct: number[];
  explanation: string;
}

export interface PackQuestion extends PackCheck {
  id: string;
  module: string;
  difficulty: 'easy' | 'medium' | 'hard';
  type: 'single' | 'multiple';
}

export interface PackLesson {
  slug: string;
  title: string;
  type: 'article' | 'video';
  durationMinutes: number;
  body: string;
  video: { script: string; src: string | null; poster: string | null; captions: string | null } | null;
  keyTakeaways: string[];
  knowledgeCheck: PackCheck[];
  activity: string | null;
}

export interface PackModule {
  slug: string;
  title: string;
  summary: string;
  lessons: PackLesson[];
}

export interface CoursePack {
  slug: string;
  version: number;
  title: string;
  subtitle: string;
  category: string;
  level: string;
  estimatedMinutes: number;
  description: string;
  outcomes: string[];
  skills: string[];
  prerequisites: string[];
  badge: { name: string; description: string; criteria: string };
  passingScore: number;
  modules: PackModule[];
  finalExam: { questionCount: number; timeLimitMinutes: number; maxAttemptsPerDay: number; pool: PackQuestion[] };
}

export interface VersionDocument {
  id: string;
  courseId: string;
  number: number;
  source: CourseSource;
  isPublished: boolean;
  document: CoursePack;
}

const base = '/admin/learning';
export const adminLearningKeys = {
  all: ['admin', 'learning'] as const,
  courses: (q: object) => ['admin', 'learning', 'courses', q] as const,
  course: (id: string) => ['admin', 'learning', 'course', id] as const,
  version: (id: string, v: string) => ['admin', 'learning', 'course', id, 'version', v] as const,
  questions: (id: string) => ['admin', 'learning', 'course', id, 'questions'] as const,
  learners: (id: string, q: object) => ['admin', 'learning', 'course', id, 'learners', q] as const,
  certificates: (q: object) => ['admin', 'learning', 'certificates', q] as const,
};

export function useAdminCourses(query: Record<string, string | number | undefined>) {
  return useQuery({
    queryKey: adminLearningKeys.courses(query),
    queryFn: ({ signal }) => api.get<PagedResult<AdminCourseRow>>(`${base}/courses`, { query, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useAdminCourse(id: string) {
  return useQuery({ queryKey: adminLearningKeys.course(id), queryFn: () => api.get<AdminCourseDetail>(`${base}/courses/${id}`) });
}

export function useCourseVersion(courseId: string, versionId: string | undefined) {
  return useQuery({
    queryKey: adminLearningKeys.version(courseId, versionId ?? ''),
    queryFn: () => api.get<VersionDocument>(`${base}/courses/${courseId}/versions/${versionId}`),
    enabled: !!versionId,
  });
}

export function useQuestionStats(id: string, enabled: boolean) {
  return useQuery({
    queryKey: adminLearningKeys.questions(id),
    queryFn: () => api.get<QuestionStat[]>(`${base}/courses/${id}/questions`),
    enabled,
  });
}

export function useLearners(id: string, query: Record<string, string | number | undefined>, enabled: boolean) {
  return useQuery({
    queryKey: adminLearningKeys.learners(id, query),
    queryFn: () => api.get<PagedResult<LearnerRow>>(`${base}/courses/${id}/learners`, { query }),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useAdminCertificates(query: Record<string, string | number | undefined>) {
  return useQuery({
    queryKey: adminLearningKeys.certificates(query),
    queryFn: ({ signal }) => api.get<PagedResult<AdminCertificate>>(`${base}/certificates`, { query, signal }),
    placeholderData: keepPreviousData,
  });
}

export function useCourseMutations(id: string) {
  const qc = useQueryClient();
  const done = (data: AdminCourseDetail) => {
    qc.setQueryData(adminLearningKeys.course(id), data);
    void qc.invalidateQueries({ queryKey: ['admin', 'learning', 'courses'] });
  };
  return {
    publish: useMutation({
      mutationFn: (v: { versionId: string; concurrencyStamp: string }) => api.post<AdminCourseDetail>(`${base}/courses/${id}/publish`, v),
      onSuccess: done,
    }),
    unpublish: useMutation({
      mutationFn: (v: { concurrencyStamp: string }) => api.post<AdminCourseDetail>(`${base}/courses/${id}/unpublish`, v),
      onSuccess: done,
    }),
    settings: useMutation({
      mutationFn: (v: { isFeatured: boolean; sortOrder: number; concurrencyStamp: string }) =>
        api.put<AdminCourseDetail>(`${base}/courses/${id}/settings`, v),
      onSuccess: done,
    }),
    saveVersion: useMutation({
      mutationFn: (v: { document: CoursePack; basedOnVersionId?: string; note?: string; publish: boolean; concurrencyStamp: string }) =>
        api.post<AdminCourseDetail>(`${base}/courses/${id}/versions`, v),
      onSuccess: done,
    }),
    video: useMutation({
      mutationFn: (v: { lessonSlug: string; src: string | null; poster: string | null; captions: string | null; publish: boolean; concurrencyStamp: string }) =>
        api.put<AdminCourseDetail>(`${base}/courses/${id}/lessons/${encodeURIComponent(v.lessonSlug)}/video`, v),
      onSuccess: done,
    }),
  };
}

export function validateDocument(document: CoursePack) {
  return api.post<ValidationReport>(`${base}/courses/validate`, { document });
}

export function createCourse(document: CoursePack, publish: boolean) {
  return api.post<AdminCourseDetail>(`${base}/courses`, { document, publish });
}

export function uploadMedia(file: File) {
  const form = new FormData();
  form.append('file', file);
  return api.upload<{ id: string; url: string; contentType: string }>(`${base}/media`, form);
}

export function revokeCertificate(id: string, body: { reason: string; confirm: boolean; concurrencyStamp: string }) {
  return api.post<AdminCertificate>(`${base}/certificates/${id}/revoke`, body);
}

export function issueCertificate(body: { userId: string; courseId: string; reason: string; confirm: boolean }) {
  return api.post<AdminCertificate>(`${base}/certificates`, body);
}
