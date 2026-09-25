import { Award, CheckCircle2, GraduationCap, PlayCircle } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { Link, useParams } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { ProgressBar } from '@/components/ui/Progress';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { errorMessage } from '@/lib/api/errors';
import { useAnswerCheck, useEnrol, useLessonProgress, useMyCourse, useMyLesson } from '@/features/learning/api';
import { CourseView } from '@/features/learning/components/CourseView';
import { LessonView } from '@/features/learning/components/LessonView';
import '@/features/learning/learning.css';
import { learningPaths } from './LearningHomePages';

export function LearningCoursePage() {
  const { slug = '' } = useParams();
  const q = useMyCourse(slug);
  const enrol = useEnrol(slug);
  const toast = useToast();

  if (q.isPending) return <Skeleton height={420} radius="var(--radius-xl)" />;
  if (q.isError)
    return (
      <Card flat>
        <ErrorState error={q.error} title="This course isn’t available" onRetry={() => void q.refetch()} />
      </Card>
    );
  const { course, progress } = q.data;
  const firstLesson = course.modules[0]?.lessons[0]?.slug;
  const resume = progress?.resumeLessonSlug ?? firstLesson;

  const actions = !progress ? (
    <Button
      size="lg"
      leadingIcon={<GraduationCap aria-hidden="true" />}
      loading={enrol.isPending}
      onClick={() =>
        enrol.mutate(undefined, {
          onSuccess: () => toast.success('You’re enrolled', 'Your progress is saved as you go.'),
          onError: (e) => toast.error('Enrolment failed', errorMessage(e)),
        })
      }
    >
      Enrol for free
    </Button>
  ) : (
    <>
      {progress.passed ? (
        progress.certificateId && (
          <ButtonLink to={learningPaths.certificate(progress.certificateId)} size="lg" leadingIcon={<Award aria-hidden="true" />}>
            View your certificate
          </ButtonLink>
        )
      ) : progress.examUnlocked ? (
        <ButtonLink to={learningPaths.exam(course.card.slug)} size="lg" leadingIcon={<Award aria-hidden="true" />}>
          Take the final assessment
        </ButtonLink>
      ) : (
        resume && (
          <ButtonLink to={learningPaths.lesson(course.card.slug, resume)} size="lg" leadingIcon={<PlayCircle aria-hidden="true" />}>
            {progress.completedLessonCount > 0 ? 'Continue learning' : 'Start the first lesson'}
          </ButtonLink>
        )
      )}
      {progress.examUnlocked && !progress.passed && resume && (
        <ButtonLink to={learningPaths.lesson(course.card.slug, resume)} size="lg" variant="secondary">
          Review lessons
        </ButtonLink>
      )}
    </>
  );

  return (
    <div className="pp-page lx-page">
      <nav aria-label="Breadcrumb" className="ui-breadcrumbs lx-crumbs">
        <ol>
          <li>
            <Link to={learningPaths.home}>My learning</Link>
          </li>
          <li aria-hidden="true">/</li>
          <li>
            <Link to={learningPaths.catalog}>All courses</Link>
          </li>
        </ol>
      </nav>
      <CourseView
        course={course}
        lessonLink={(l) => learningPaths.lesson(course.card.slug, l)}
        courseLink={learningPaths.course}
        actions={actions}
        completedLessons={progress ? new Set(progress.completedLessons) : undefined}
        progress={
          progress && (
            <div className="lx-hero__progress">
              <ProgressBar
                value={progress.progressPercent}
                label="Course progress"
                valueText={`${progress.completedLessonCount} of ${progress.lessonCount} lessons (${progress.progressPercent}%)`}
              />
              {progress.passed && (
                <p className="lx-ok">
                  <CheckCircle2 aria-hidden="true" /> Passed{progress.bestScore !== null ? ` with ${progress.bestScore}%` : ''}
                </p>
              )}
            </div>
          )
        }
      />
    </div>
  );
}

export function LearningLessonPage() {
  const { slug = '', lessonSlug = '' } = useParams();
  const q = useMyLesson(slug, lessonSlug);
  const { start, complete } = useLessonProgress(slug, lessonSlug);
  const answer = useAnswerCheck(slug, lessonSlug);
  const enrol = useEnrol(slug);
  const toast = useToast();
  const started = useRef<string | null>(null);

  const enrolled = q.data?.enrolled ?? false;
  // Opening a lesson records the resume point (once per lesson view).
  useEffect(() => {
    if (!enrolled || started.current === lessonSlug) return;
    started.current = lessonSlug;
    start.mutate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enrolled, lessonSlug]);

  if (q.isPending) return <Skeleton height={520} radius="var(--radius-xl)" />;
  if (q.isError)
    return (
      <Card flat>
        <ErrorState error={q.error} title="This lesson isn’t available" onRetry={() => void q.refetch()} />
      </Card>
    );

  const { lesson, completed, answers, progress } = q.data;
  const footer = !enrolled ? (
    <Card className="lx-cta">
      <CardBody>
        <p>Enrol for free to save your progress, take the final assessment and earn the certificate.</p>
        <Button loading={enrol.isPending} onClick={() => enrol.mutate(undefined, { onSuccess: () => void q.refetch() })}>
          Enrol for free
        </Button>
      </CardBody>
    </Card>
  ) : (
    <Card className="lx-complete">
      <CardBody>
        {completed ? (
          <p className="lx-ok" role="status">
            <CheckCircle2 aria-hidden="true" /> Lesson completed
          </p>
        ) : (
          <Button
            leadingIcon={<CheckCircle2 aria-hidden="true" />}
            loading={complete.isPending}
            onClick={() =>
              complete.mutate(undefined, {
                onSuccess: (p) => {
                  void q.refetch();
                  toast.success(
                    'Lesson completed',
                    p.examUnlocked ? 'Every lesson is done — the final assessment is unlocked.' : `${p.progressPercent}% of the course done.`,
                  );
                },
                onError: (e) => toast.error('Couldn’t save your progress', errorMessage(e)),
              })
            }
          >
            Mark lesson complete
          </Button>
        )}
        {progress?.examUnlocked && !progress.passed && (
          <ButtonLink to={learningPaths.exam(slug)} variant="secondary" leadingIcon={<Award aria-hidden="true" />}>
            Take the final assessment
          </ButtonLink>
        )}
      </CardBody>
    </Card>
  );

  return (
    <div className="pp-page lx-page">
      <LessonView
        lesson={lesson}
        courseLink={learningPaths.course(slug)}
        lessonLink={(l) => learningPaths.lesson(slug, l)}
        savedAnswers={answers}
        onAnswer={enrolled ? (index, selected) => answer.mutateAsync({ index, selected }) : undefined}
        footer={footer}
        header={
          progress && (
            <ProgressBar
              value={progress.progressPercent}
              label="Course progress"
              valueText={`${progress.completedLessonCount} of ${progress.lessonCount} lessons`}
              className="lx-lesson__progress"
            />
          )
        }
      />
    </div>
  );
}
