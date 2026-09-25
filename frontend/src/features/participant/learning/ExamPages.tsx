import clsx from 'clsx';
import { Award, CheckCircle2, Clock, RotateCcw, Timer, XCircle } from 'lucide-react';
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ButtonLink } from '@/components/ui/ButtonLink';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DateTime } from '@/components/ui/DateTime';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { ProgressRing } from '@/components/ui/Progress';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/toastContext';
import { errorMessage, isApiError } from '@/lib/api/errors';
import {
  BLOCKED_REASONS,
  useAttempt,
  useExamOverview,
  useSaveAnswer,
  useStartExam,
  useSubmitAttempt,
  type AttemptQuestion,
  type ExamAttempt,
} from '@/features/learning/api';
import { LearnSlot } from '@/features/learning/components/LearnSlot';
import '@/features/learning/learning.css';
import { learningPaths } from './LearningHomePages';

/** Final assessment overview: rules, attempts left, history, start / continue. */
export function ExamOverviewPage() {
  const { slug = '' } = useParams();
  const q = useExamOverview(slug);
  const start = useStartExam(slug);
  const navigate = useNavigate();
  const toast = useToast();

  if (q.isPending) return <Skeleton height={360} radius="var(--radius-xl)" />;
  if (q.isError)
    return (
      <Card flat>
        <ErrorState error={q.error} title="The assessment isn’t available" onRetry={() => void q.refetch()} />
      </Card>
    );
  const o = q.data;
  return (
    <div className="pp-page ui-dash lx-page">
      <PageHeader
        eyebrow="Final assessment"
        title={o.courseTitle}
        breadcrumbs={[
          { label: 'My learning', to: learningPaths.home },
          { label: o.courseTitle, to: learningPaths.course(o.courseSlug) },
          { label: 'Final assessment' },
        ]}
      />
      <Card as="section" aria-labelledby="rules-heading">
        <CardHeader title="How it works" titleId="rules-heading" />
        <CardBody>
          <ul className="lx-facts lx-facts--row">
            <li>
              <Award aria-hidden="true" /> {o.rules.questionCount} questions, drawn at random for each attempt
            </li>
            <li>
              <Timer aria-hidden="true" /> {o.rules.timeLimitMinutes} minutes, timed by the server
            </li>
            <li>
              <CheckCircle2 aria-hidden="true" /> Pass mark {o.rules.passingScore}%
            </li>
            <li>
              <RotateCcw aria-hidden="true" /> {o.rules.maxAttemptsPerDay} attempts per 24 hours ({o.attemptsRemaining} left)
            </li>
          </ul>
          <p className="lx-muted">
            Your answers are saved as you go. When the time runs out, the answers saved so far are graded. After you submit, you’ll see
            which answers were right with an explanation for each question.
          </p>
          {o.blockedReason && o.blockedReason !== 'learning.attempt_in_progress' && (
            <Alert tone={o.blockedReason === 'learning.already_certified' ? 'success' : 'info'}>
              {BLOCKED_REASONS[o.blockedReason] ?? 'The assessment is not available right now.'}
              {o.blockedReason === 'learning.attempt_limit' && o.nextAttemptAt && (
                <>
                  {' '}
                  Next attempt from <DateTime value={o.nextAttemptAt} />.
                </>
              )}
            </Alert>
          )}
          <div className="lx-actions">
            {o.activeAttemptId ? (
              <ButtonLink to={learningPaths.attempt(o.activeAttemptId)} size="lg">
                Continue your attempt
              </ButtonLink>
            ) : o.canStart ? (
              <Button
                size="lg"
                loading={start.isPending}
                onClick={() =>
                  start.mutate(undefined, {
                    onSuccess: (attempt) => navigate(learningPaths.attempt(attempt.id)),
                    onError: (e) => {
                      toast.error('The attempt couldn’t start', errorMessage(e));
                      void q.refetch();
                    },
                  })
                }
              >
                Start the assessment
              </Button>
            ) : null}
            {o.certificateId && (
              <ButtonLink to={learningPaths.certificate(o.certificateId)} size="lg" leadingIcon={<Award aria-hidden="true" />}>
                View your certificate
              </ButtonLink>
            )}
            {o.blockedReason === 'learning.lessons_incomplete' && (
              <ButtonLink to={learningPaths.course(o.courseSlug)} variant="secondary">
                Back to the lessons
              </ButtonLink>
            )}
          </div>
        </CardBody>
      </Card>
      {o.attempts.length > 0 && (
        <Card as="section" aria-labelledby="attempts-heading">
          <CardHeader title="Your attempts" titleId="attempts-heading" />
          <CardBody flush>
            <ul className="lx-rows">
              {o.attempts.map((a) => (
                <li key={a.id} className="lx-row">
                  <div className="lx-row__text">
                    <Link to={learningPaths.attempt(a.id)} className="lx-row__title">
                      <DateTime value={a.startedAt} />
                    </Link>
                    <span className="lx-muted">
                      {a.status === 'InProgress' ? 'In progress' : a.status === 'Expired' ? 'Time ran out' : 'Submitted'}
                    </span>
                  </div>
                  {a.score !== null && (
                    <Badge tone={a.passed ? 'success' : 'warning'}>
                      {a.score}% · {a.passed ? 'Passed' : 'Not passed'}
                    </Badge>
                  )}
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}
    </div>
  );
}

function formatClock(seconds: number): string {
  const s = Math.max(0, seconds);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

/** An attempt: timed questions while in progress, the result and per-question review once finished. */
export function ExamAttemptPage() {
  const { attemptId = '' } = useParams();
  const q = useAttempt(attemptId);
  if (q.isPending) return <Skeleton height={480} radius="var(--radius-xl)" />;
  if (q.isError)
    return (
      <Card flat>
        <ErrorState error={q.error} title="This attempt isn’t available" onRetry={() => void q.refetch()} />
      </Card>
    );
  return q.data.status === 'InProgress' ? <TakeAttempt attempt={q.data} /> : <AttemptReview attempt={q.data} />;
}

function TakeAttempt({ attempt }: { attempt: ExamAttempt }) {
  const save = useSaveAnswer(attempt.id);
  const submit = useSubmitAttempt(attempt.id);
  const toast = useToast();
  const [answers, setAnswers] = useState<Record<string, number[]>>(() =>
    Object.fromEntries(attempt.questions.map((q) => [q.id, q.selected])),
  );
  const [current, setCurrent] = useState(0);
  const [confirmOpen, setConfirmOpen] = useState(false);
  // The deadline is measured against the server's clock (offset = server now − browser now when the attempt loaded).
  const offset = useMemo(() => new Date(attempt.serverNow).getTime() - Date.now(), [attempt.serverNow]);
  const deadline = new Date(attempt.deadlineAt).getTime();
  const [left, setLeft] = useState(() => Math.ceil((deadline - (Date.now() + offset)) / 1000));
  const submitted = useRef(false);
  const headingRef = useRef<HTMLHeadingElement>(null);

  const doSubmit = () => {
    if (submitted.current) return;
    submitted.current = true;
    submit.mutate(
      Object.entries(answers).map(([questionId, selected]) => ({ questionId, selected })),
      {
        onError: (e) => {
          submitted.current = false;
          toast.error('Your attempt wasn’t submitted', errorMessage(e));
        },
      },
    );
  };

  useEffect(() => {
    const timer = window.setInterval(() => {
      const remaining = Math.ceil((deadline - (Date.now() + offset)) / 1000);
      setLeft(remaining);
      if (remaining <= 0) doSubmit();
    }, 1000);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deadline, offset]);

  useEffect(() => headingRef.current?.focus(), [current]);

  const question = attempt.questions[current]!;
  const answeredCount = attempt.questions.filter((q) => (answers[q.id]?.length ?? 0) > 0).length;
  const unanswered = attempt.questions.length - answeredCount;

  const choose = (q: AttemptQuestion, position: number) => {
    const prev = answers[q.id] ?? [];
    const next = q.type === 'Multiple' ? (prev.includes(position) ? prev.filter((p) => p !== position) : [...prev, position]) : [position];
    setAnswers({ ...answers, [q.id]: next });
    save.mutate(
      { questionId: q.id, selected: next },
      {
        onError: (e) => {
          if (isApiError(e) && e.code === 'learning.time_expired') doSubmit();
          else toast.error('Your answer wasn’t saved', errorMessage(e));
        },
      },
    );
  };

  const lowTime = left <= 60;
  return (
    <div className="pp-page lx-page lx-exam">
      <header className="lx-exam__bar">
        <div>
          <p className="lx-muted">Final assessment</p>
          <p className="lx-exam__course">{attempt.courseTitle}</p>
        </div>
        <div className={clsx('lx-timer', lowTime && 'is-low')} role="timer" aria-label="Time left">
          <Clock aria-hidden="true" />
          <span>{formatClock(left)}</span>
          {/* Announce only at the one-minute mark, not every second. */}
          <span className="visually-hidden" aria-live="assertive">
            {left <= 60 && left > 55 ? 'One minute left' : ''}
          </span>
        </div>
      </header>

      <nav className="lx-qnav" aria-label="Questions">
        <ol>
          {attempt.questions.map((q, i) => {
            const answered = (answers[q.id]?.length ?? 0) > 0;
            return (
              <li key={q.id}>
                <button
                  type="button"
                  className={clsx('lx-qnav__btn', answered && 'is-answered', i === current && 'is-current')}
                  aria-current={i === current ? 'step' : undefined}
                  onClick={() => setCurrent(i)}
                >
                  {i + 1}
                  <span className="visually-hidden">{answered ? ' (answered)' : ' (not answered)'}</span>
                </button>
              </li>
            );
          })}
        </ol>
        <p className="lx-muted" aria-live="polite">
          {answeredCount} of {attempt.questions.length} answered
        </p>
      </nav>

      <Card as="section" aria-labelledby="question-heading" className="lx-question">
        <CardBody>
          <fieldset className="lx-check">
            <legend>
              <h1 id="question-heading" ref={headingRef} tabIndex={-1} className="lx-question__title">
                <span className="lx-question__num">
                  Question {question.number} of {attempt.questions.length}
                </span>
                {question.question}
              </h1>
              {question.type === 'Multiple' && <p className="lx-check__hint">Choose all that apply.</p>}
            </legend>
            <QuestionOptions question={question} selected={answers[question.id] ?? []} onChoose={(p) => choose(question, p)} />
          </fieldset>
          <div className="lx-question__nav">
            <Button variant="secondary" disabled={current === 0} onClick={() => setCurrent(current - 1)}>
              Previous question
            </Button>
            {current < attempt.questions.length - 1 ? (
              <Button onClick={() => setCurrent(current + 1)}>Next question</Button>
            ) : (
              <Button onClick={() => setConfirmOpen(true)} loading={submit.isPending}>
                Submit answers
              </Button>
            )}
          </div>
        </CardBody>
      </Card>
      <div className="lx-actions">
        <Button variant="ghost" onClick={() => setConfirmOpen(true)} loading={submit.isPending}>
          Submit now
        </Button>
      </div>
      <ConfirmDialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        title="Submit your answers?"
        description={
          unanswered > 0
            ? `${unanswered} question${unanswered === 1 ? ' is' : 's are'} not answered and will count as wrong.`
            : 'You answered every question. You can’t change your answers after submitting.'
        }
        confirmLabel="Submit"
        onConfirm={() => {
          setConfirmOpen(false);
          doSubmit();
        }}
      />
    </div>
  );
}

function QuestionOptions({
  question,
  selected,
  onChoose,
  review,
}: {
  question: AttemptQuestion;
  selected: number[];
  onChoose?: (position: number) => void;
  review?: boolean;
}) {
  const name = useId();
  return (
    <div className="lx-check__options">
      {question.options.map((option, i) => {
        const id = `${name}-${i}`;
        const correct = review && question.review?.correct.includes(i);
        const wrong = review && !correct && selected.includes(i);
        return (
          <label key={i} htmlFor={id} className={clsx('lx-option', selected.includes(i) && 'is-selected', correct && 'is-correct', wrong && 'is-wrong')}>
            <input
              id={id}
              type={question.type === 'Multiple' ? 'checkbox' : 'radio'}
              name={name}
              checked={selected.includes(i)}
              disabled={review}
              onChange={() => onChoose?.(i)}
            />
            <span>{option}</span>
            {correct && <span className="lx-option__tag">Correct answer</span>}
            {wrong && <span className="lx-option__tag lx-option__tag--bad">Your answer</span>}
          </label>
        );
      })}
    </div>
  );
}

function AttemptReview({ attempt }: { attempt: ExamAttempt }) {
  const r = attempt.result!;
  return (
    <div className="pp-page ui-dash lx-page">
      <PageHeader
        eyebrow="Final assessment result"
        title={attempt.courseTitle}
        breadcrumbs={[
          { label: 'My learning', to: learningPaths.home },
          { label: attempt.courseTitle, to: learningPaths.course(attempt.courseSlug) },
          { label: 'Result' },
        ]}
      />
      <Card className={clsx('lx-result', r.passed ? 'lx-result--pass' : 'lx-result--fail')}>
        <CardBody>
          <div className="lx-result__row">
            <ProgressRing value={r.score} label={`Score ${r.score}%`} size={112} strokeWidth={10} centerText={`${r.score}%`} />
            <div>
              <h2 className="lx-result__title" data-testid="exam-outcome">
                {r.passed ? 'You passed!' : 'Not passed this time'}
              </h2>
              <p>
                {r.correctCount} of {r.questionCount} correct · pass mark {r.passingScore}%
                {attempt.status === 'Expired' ? ' · the time ran out, so your saved answers were graded' : ''}
              </p>
              <div className="lx-actions">
                {r.passed && r.certificateId && (
                  <ButtonLink to={learningPaths.certificate(r.certificateId)} leadingIcon={<Award aria-hidden="true" />}>
                    Get your certificate
                  </ButtonLink>
                )}
                {!r.passed && (
                  <ButtonLink to={learningPaths.exam(attempt.courseSlug)} leadingIcon={<RotateCcw aria-hidden="true" />}>
                    {r.attemptsRemaining > 0 ? `Retake (${r.attemptsRemaining} left today)` : 'See when you can retake'}
                  </ButtonLink>
                )}
                <ButtonLink to={learningPaths.course(attempt.courseSlug)} variant="secondary">
                  Back to the course
                </ButtonLink>
              </div>
            </div>
          </div>
        </CardBody>
      </Card>
      {/* Partner slot (exam result). */}
      <LearnSlot slot="learn.exam" keywords={[attempt.courseTitle]} categories={[]} />
      <section aria-labelledby="review-heading" className="stack">
        <h2 id="review-heading" className="ui-dash-head">
          Review your answers
        </h2>
        <ol className="lx-review">
          {attempt.questions.map((q) => (
            <li key={q.id}>
              <Card className={clsx('lx-review__item', q.review?.isCorrect ? 'is-correct' : 'is-wrong')}>
                <CardBody>
                  <fieldset className="lx-check">
                    <legend className="lx-check__question">
                      <span className="lx-review__mark">
                        {q.review?.isCorrect ? <CheckCircle2 aria-hidden="true" /> : <XCircle aria-hidden="true" />}
                        {q.review?.isCorrect ? 'Correct' : 'Incorrect'}
                      </span>{' '}
                      {q.number}. {q.question}
                    </legend>
                    <QuestionOptions question={q} selected={q.selected} review />
                  </fieldset>
                  <p className="lx-explanation">
                    <strong>Why: </strong>
                    {q.review?.explanation}
                  </p>
                </CardBody>
              </Card>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
