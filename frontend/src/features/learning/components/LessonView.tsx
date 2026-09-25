import clsx from 'clsx';
import { ArrowLeft, ArrowRight, CheckCircle2, Clapperboard, Lightbulb, ListChecks, XCircle } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { Markdown } from '@/features/public/site/Markdown';
import type { KnowledgeCheck as Check, KnowledgeCheckResult, Lesson } from '../api';
import { categoryClass } from './CourseCard';

export interface LessonViewProps {
  lesson: Lesson;
  lessonLink: (slug: string) => string;
  courseLink: string;
  /** Records an answer (portal) and returns the server's result; the public site checks answers in the browser. */
  onAnswer?: (index: number, selected: number[]) => Promise<KnowledgeCheckResult>;
  savedAnswers?: KnowledgeCheckResult[];
  /** Mark-complete button / sign-up CTA placed under the lesson. */
  footer?: ReactNode;
  /** Shown above the lesson title (e.g. progress). */
  header?: ReactNode;
}

/** The lesson player: video (or "video coming soon"), Markdown article, key takeaways, knowledge check, activity and prev/next. */
export function LessonView({ lesson, lessonLink, courseLink, onAnswer, savedAnswers, footer, header }: LessonViewProps) {
  return (
    <article className={clsx('lx-lesson', categoryClass(lesson.category))} aria-labelledby="lesson-title">
      <header className="lx-lesson__header">
        <p className="lx-lesson__crumb">
          <Link to={courseLink}>{lesson.courseTitle}</Link> · {lesson.moduleTitle} · Lesson {lesson.position} of {lesson.lessonCount}
        </p>
        <h1 id="lesson-title" className="lx-lesson__title">
          {lesson.title}
        </h1>
        <p className="lx-muted">
          {lesson.type === 'Video' ? 'Video lesson' : 'Article'} · {lesson.durationMinutes} min
        </p>
        {header}
      </header>

      {lesson.type === 'Video' && lesson.video && <LessonVideo lesson={lesson} />}

      <Card as="section" aria-label="Lesson" className="lx-lesson__body">
        <CardBody>
          <Markdown source={lesson.body} minLevel={2} />
        </CardBody>
      </Card>

      {lesson.keyTakeaways.length > 0 && (
        <Card as="section" aria-labelledby="takeaways-heading" className="lx-takeaways">
          <CardHeader title="Key takeaways" titleId="takeaways-heading" />
          <CardBody>
            <ul>
              {lesson.keyTakeaways.map((t) => (
                <li key={t}>
                  <Lightbulb aria-hidden="true" />
                  <span>{t}</span>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}

      {lesson.knowledgeCheck.length > 0 && (
        <Card as="section" aria-labelledby="check-heading">
          <CardHeader
            title="Check your understanding"
            titleId="check-heading"
            description="Quick questions to lock in the lesson. They don’t count towards your certificate."
          />
          <CardBody>
            <ol className="lx-checks">
              {lesson.knowledgeCheck.map((q) => (
                <li key={q.index}>
                  <KnowledgeCheckQuestion
                    check={q}
                    onAnswer={onAnswer}
                    saved={savedAnswers?.find((a) => a.index === q.index)}
                  />
                </li>
              ))}
            </ol>
          </CardBody>
        </Card>
      )}

      {lesson.activity && (
        <Card as="section" aria-labelledby="activity-heading" className="lx-activity">
          <CardHeader title="Put it into practice" titleId="activity-heading" />
          <CardBody>
            <p>
              <ListChecks aria-hidden="true" /> {lesson.activity}
            </p>
          </CardBody>
        </Card>
      )}

      {footer}

      <nav className="lx-lesson__nav" aria-label="Lessons">
        {lesson.previous ? (
          <Link to={lessonLink(lesson.previous.slug)} className="lx-navlink" rel="prev">
            <ArrowLeft aria-hidden="true" />
            <span>
              <span className="lx-navlink__label">Previous</span>
              {lesson.previous.title}
            </span>
          </Link>
        ) : (
          <span />
        )}
        {lesson.next ? (
          <Link to={lessonLink(lesson.next.slug)} className="lx-navlink lx-navlink--next" rel="next">
            <span>
              <span className="lx-navlink__label">Next</span>
              {lesson.next.title}
            </span>
            <ArrowRight aria-hidden="true" />
          </Link>
        ) : (
          <Link to={courseLink} className="lx-navlink lx-navlink--next">
            <span>
              <span className="lx-navlink__label">Finished the lessons?</span>
              Back to the course
            </span>
            <ArrowRight aria-hidden="true" />
          </Link>
        )}
      </nav>
    </article>
  );
}

function LessonVideo({ lesson }: { lesson: Lesson }) {
  const video = lesson.video!;
  const [showTranscript, setShowTranscript] = useState(false);
  const transcriptId = useId();
  return (
    <section className="lx-video" aria-label="Lesson video">
      {video.src ? (
        <video className="lx-video__player" controls preload="metadata" poster={video.poster ?? undefined} playsInline>
          <source src={video.src} type="video/mp4" />
          {video.captions && <track kind="captions" src={video.captions} srcLang="en" label="English" default />}
          Your browser can’t play this video. Read the lesson below instead.
        </video>
      ) : (
        <div className="lx-video__soon">
          <Clapperboard aria-hidden="true" />
          <div>
            <p className="lx-video__soon-title">Video coming soon</p>
            <p className="lx-muted">This lesson’s video is in production. Everything it covers is in the article below.</p>
          </div>
        </div>
      )}
      {video.transcript && (
        <div className="lx-video__transcript">
          <Button variant="ghost" size="sm" aria-expanded={showTranscript} aria-controls={transcriptId} onClick={() => setShowTranscript(!showTranscript)}>
            {showTranscript ? 'Hide transcript' : 'Show transcript'}
          </Button>
          <div id={transcriptId} hidden={!showTranscript} className="lx-video__transcript-text">
            {video.transcript.split(/\n{2,}/).map((p, i) => (
              <p key={i}>{p}</p>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

/** One knowledge-check question with immediate feedback (radio for single answers, checkboxes for "choose all"). */
export function KnowledgeCheckQuestion({
  check,
  onAnswer,
  saved,
}: {
  check: Check;
  onAnswer?: (index: number, selected: number[]) => Promise<KnowledgeCheckResult>;
  saved?: KnowledgeCheckResult;
}) {
  const name = useId();
  const [selected, setSelected] = useState<number[]>(saved?.selected ?? []);
  const [result, setResult] = useState<KnowledgeCheckResult | null>(saved ?? null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = (i: number) => {
    setResult(null);
    setSelected(check.multiple ? (selected.includes(i) ? selected.filter((x) => x !== i) : [...selected, i]) : [i]);
  };

  const submit = async () => {
    setError(null);
    const sorted = [...selected].sort((a, b) => a - b);
    if (!onAnswer) {
      const correct = [...check.correct].sort((a, b) => a - b);
      setResult({
        index: check.index,
        selected: sorted,
        isCorrect: correct.length === sorted.length && correct.every((c, i) => c === sorted[i]),
        correct,
        explanation: check.explanation,
      });
      return;
    }
    setBusy(true);
    try {
      setResult(await onAnswer(check.index, sorted));
    } catch {
      setError('Your answer couldn’t be saved. Try again.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <fieldset className="lx-check">
      <legend className="lx-check__question">
        {check.question}
        {check.multiple && <span className="lx-check__hint"> Choose all that apply.</span>}
      </legend>
      <div className="lx-check__options">
        {check.options.map((option, i) => {
          const id = `${name}-${i}`;
          const state = result ? (result.correct.includes(i) ? 'correct' : selected.includes(i) ? 'wrong' : null) : null;
          return (
            <label key={i} htmlFor={id} className={clsx('lx-option', selected.includes(i) && 'is-selected', state && `is-${state}`)}>
              <input
                id={id}
                type={check.multiple ? 'checkbox' : 'radio'}
                name={name}
                checked={selected.includes(i)}
                onChange={() => toggle(i)}
              />
              <span>{option}</span>
              {state === 'correct' && <span className="visually-hidden"> (correct answer)</span>}
            </label>
          );
        })}
      </div>
      <div className="lx-check__actions">
        <Button size="sm" variant="secondary" onClick={() => void submit()} disabled={selected.length === 0} loading={busy}>
          Check answer
        </Button>
      </div>
      <div aria-live="polite">
        {error && <p className="lx-error">{error}</p>}
        {result && (
          <div className={clsx('lx-feedback', result.isCorrect ? 'lx-feedback--ok' : 'lx-feedback--bad')}>
            <p className="lx-feedback__title">
              {result.isCorrect ? <CheckCircle2 aria-hidden="true" /> : <XCircle aria-hidden="true" />}
              {result.isCorrect ? 'Correct' : 'Not quite'}
            </p>
            <p>{result.explanation}</p>
          </div>
        )}
      </div>
    </fieldset>
  );
}
