import '../academy.css';
import clsx from 'clsx';
import { Captions, ChevronDown, Clapperboard, FileText, Headphones, ListVideo, PlayCircle, Sparkles } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState, type CSSProperties } from 'react';
import { formatClock, type LectureChapter, type LessonLecture } from '../api';

const SPEEDS = [0.75, 1, 1.25, 1.5, 2] as const;

export interface LectureSectionProps {
  lecture: LessonLecture;
  /** Heading level of the section title (the lesson title is the page h1). */
  headingLevel?: 2 | 3;
}

/**
 * The lesson's video lecture (pack v2).
 *
 * - Produced (`src`): an HTML5 player with poster and captions track, chapters from the scenes (click to seek; times are
 *   scaled from the scene plan to the real video length), the active chapter with its progress, playback speed
 *   (segmented control), and the transcript.
 * - Not produced yet: a "coming soon" stage that previews each chapter's slide, the chapter list and the full transcript
 *   (the narration), so the lecture is useful today and the page is complete for search engines (the server-rendered
 *   page carries the same transcript).
 */
export function LectureSection({ lecture, headingLevel = 2 }: LectureSectionProps) {
  const Heading = `h${headingLevel}` as const;
  const titleId = useId();
  const transcriptId = useId();
  const videoRef = useRef<HTMLVideoElement>(null);
  const transcriptRef = useRef<HTMLDivElement>(null);
  const [active, setActive] = useState(0);
  const [chapterProgress, setChapterProgress] = useState(0);
  const [duration, setDuration] = useState<number | null>(null);
  const [speed, setSpeed] = useState<(typeof SPEEDS)[number]>(1);
  const [transcriptOpen, setTranscriptOpen] = useState(false);
  const chapters = lecture.chapters;
  const planned = Math.max(1, lecture.totalSeconds);
  // Planned scene times → real video times (the produced video rarely matches the plan to the second).
  const scale = duration && Number.isFinite(duration) && duration > 0 ? duration / planned : 1;
  const startOf = useCallback((c: LectureChapter) => c.startSeconds * scale, [scale]);

  useEffect(() => {
    if (videoRef.current) videoRef.current.playbackRate = speed;
  }, [speed]);

  const onTime = () => {
    const v = videoRef.current;
    if (!v) return;
    const t = v.currentTime;
    let index = 0;
    for (let i = 0; i < chapters.length; i++) if (t >= startOf(chapters[i]!) - 0.25) index = i;
    setActive(index);
    const c = chapters[index]!;
    const len = Math.max(1, c.seconds * scale);
    setChapterProgress(Math.min(1, Math.max(0, (t - startOf(c)) / len)));
  };

  const openTranscriptAt = (index: number) => {
    setTranscriptOpen(true);
    // After the panel opens: bring the chapter's transcript into view (no smooth scroll with reduced motion: base.css).
    window.setTimeout(() => {
      transcriptRef.current?.querySelector<HTMLElement>(`[data-chapter="${index}"]`)?.scrollIntoView({ block: 'nearest' });
    }, 50);
  };

  const selectChapter = (index: number) => {
    setActive(index);
    const v = videoRef.current;
    if (lecture.produced && v) {
      v.currentTime = startOf(chapters[index]!);
      setChapterProgress(0);
      void v.play?.()?.catch?.(() => undefined);
    }
  };

  const current = chapters[active];

  return (
    <section className={clsx('lx-lecture', lecture.produced ? 'is-produced' : 'is-soon')} aria-labelledby={titleId}>
      <header className="lx-lecture__head">
        <span className="lx-lecture__icon" aria-hidden="true">
          <Clapperboard />
        </span>
        <div className="lx-lecture__heading">
          <p className="lx-lecture__kicker">Video lecture</p>
          <Heading id={titleId} className="lx-lecture__title">
            {lecture.title}
          </Heading>
          <p className="lx-lecture__meta">
            {chapters.length} chapters · about {lecture.targetMinutes} min
            {lecture.captions ? ' · captions' : ''} · full transcript
          </p>
        </div>
        <span className={clsx('lx-lecture__status', lecture.produced ? 'is-live' : 'is-soon')}>
          {lecture.produced ? (
            <>
              <PlayCircle aria-hidden="true" /> Watch now
            </>
          ) : (
            <>
              <Sparkles aria-hidden="true" /> Coming soon
            </>
          )}
        </span>
      </header>

      {lecture.produced && lecture.src ? (
        <div className="lx-lecture__stage">
          {/* Captions come from the lecture's WebVTT track when present; the full transcript is always below. */}
          {/* eslint-disable-next-line jsx-a11y/media-has-caption */}
          <video
            ref={videoRef}
            className="lx-lecture__video"
            controls
            preload="metadata"
            playsInline
            poster={lecture.poster ?? undefined}
            onLoadedMetadata={(e) => setDuration(e.currentTarget.duration)}
            onTimeUpdate={onTime}
            aria-describedby={transcriptId}
          >
            <source src={lecture.src} type="video/mp4" />
            {lecture.captions && <track kind="captions" src={lecture.captions} srcLang="en" label="English" default />}
            Your browser can’t play this video. Read the transcript below instead.
          </video>
        </div>
      ) : (
        <div className="lx-lecture__soon">
          <div className="lx-lecture__slide" aria-live="polite">
            <div className="lx-lecture__slide-glow" aria-hidden="true" />
            <p className="lx-lecture__slide-step">
              Chapter {active + 1} of {chapters.length}
            </p>
            <p className="lx-lecture__slide-title">{current?.title}</p>
            {current && current.points.length > 0 && (
              <ul className="lx-lecture__slide-points">
                {current.points.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            )}
            <div className="lx-lecture__slide-dots" aria-hidden="true">
              {chapters.map((c) => (
                <span key={c.index} className={clsx(c.index === active && 'is-on', c.index < active && 'is-past')} />
              ))}
            </div>
          </div>
          <div className="lx-lecture__soon-text">
            <p className="lx-lecture__soon-title">
              <Headphones aria-hidden="true" /> The narrated lecture is in production
            </p>
            <p className="lx-muted">
              Every chapter is scripted and ready. Browse the chapters and read the full transcript now — the video will
              appear here when it’s published.
            </p>
          </div>
        </div>
      )}

      <div className="lx-lecture__controls">
        {lecture.produced && (
          <div className="lx-seg" role="radiogroup" aria-label="Playback speed">
            <span
              className="lx-seg__thumb"
              aria-hidden="true"
              style={{ ['--seg-i' as string]: SPEEDS.indexOf(speed), ['--seg-n' as string]: SPEEDS.length } as CSSProperties}
            />
            {SPEEDS.map((s) => (
              <button
                key={s}
                type="button"
                role="radio"
                aria-checked={speed === s}
                className={clsx('lx-seg__opt', speed === s && 'is-on')}
                onClick={() => setSpeed(s)}
              >
                {s}×
              </button>
            ))}
          </div>
        )}
        <button
          type="button"
          className="lx-lecture__toggle"
          aria-expanded={transcriptOpen}
          aria-controls={transcriptId}
          onClick={() => setTranscriptOpen(!transcriptOpen)}
        >
          <FileText aria-hidden="true" />
          {transcriptOpen ? 'Hide transcript' : 'Read the transcript'}
          <span className="lx-muted"> · {lecture.transcriptWords.toLocaleString('en')} words</span>
          <ChevronDown aria-hidden="true" className="lx-lecture__chevron" />
        </button>
      </div>

      <div className="lx-lecture__chapters">
        <p className="lx-lecture__chapters-label">
          <ListVideo aria-hidden="true" /> Chapters
        </p>
        <ol className="lx-chapters">
          {chapters.map((c) => {
            const isActive = c.index === active;
            return (
              <li key={c.index} className={clsx('lx-chapter', isActive && 'is-active', c.index < active && 'is-past')}>
                <button
                  type="button"
                  className="lx-chapter__btn"
                  aria-current={isActive ? 'step' : undefined}
                  onClick={() => selectChapter(c.index)}
                >
                  <span className="lx-chapter__time">{formatClock(startOf(c))}</span>
                  <span className="lx-chapter__title">{c.title}</span>
                  <span className="lx-chapter__bar" aria-hidden="true">
                    <span style={{ transform: `scaleX(${isActive ? (lecture.produced ? chapterProgress : 1) : c.index < active ? 1 : 0})` }} />
                  </span>
                </button>
                {!lecture.produced && (
                  <button type="button" className="lx-chapter__read" onClick={() => openTranscriptAt(c.index)}>
                    Read<span className="visually-hidden"> the transcript of “{c.title}”</span>
                  </button>
                )}
              </li>
            );
          })}
        </ol>
      </div>

      <div
        id={transcriptId}
        ref={transcriptRef}
        className={clsx('lx-transcript', transcriptOpen && 'is-open')}
        role="region"
        aria-label="Lecture transcript"
        hidden={!transcriptOpen}
      >
        <div className="lx-transcript__inner">
          <p className="lx-transcript__note">
            <Captions aria-hidden="true" /> Transcript of the narration, chapter by chapter.
          </p>
          {chapters.map((c) => (
            <section key={c.index} className="lx-transcript__chapter" data-chapter={c.index} aria-label={c.title}>
              <p className="lx-transcript__head">
                <span className="lx-transcript__time">{formatClock(startOf(c))}</span> {c.title}
              </p>
              <p className="lx-transcript__text">{c.narration}</p>
            </section>
          ))}
        </div>
      </div>
    </section>
  );
}
