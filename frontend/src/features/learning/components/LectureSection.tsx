import '../academy.css';
import clsx from 'clsx';
import {
  Captions,
  ChevronDown,
  Clapperboard,
  FileText,
  Headphones,
  ListVideo,
  Play,
  PlayCircle,
  Sparkles,
} from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState, type CSSProperties } from 'react';
import { formatClock, type LectureChapter, type LessonLecture } from '../api';

const SPEEDS = [0.75, 1, 1.25, 1.5, 2] as const;
type Speed = (typeof SPEEDS)[number];

/** The privacy-enhanced YouTube host: the only frame origin the CSP allows (nginx snippets/security-headers.conf). */
export const YOUTUBE_EMBED_ORIGIN = 'https://www.youtube-nocookie.com';

/** i.ytimg.com thumbnail (maxresdefault exists only for HD uploads; hqdefault always exists). */
export function youTubeThumbnail(id: string, size: 'maxresdefault' | 'hqdefault' = 'maxresdefault'): string {
  return `https://i.ytimg.com/vi/${encodeURIComponent(id)}/${size}.jpg`;
}

/** The embed URL started at `start` seconds, with the JS API enabled for postMessage control (no YouTube script in our page). */
export function youTubeEmbedSrc(embedUrl: string, start: number, origin: string): string {
  const url = new URL(embedUrl);
  url.searchParams.set('autoplay', '1');
  url.searchParams.set('enablejsapi', '1');
  url.searchParams.set('rel', '0');
  url.searchParams.set('playsinline', '1');
  if (start > 0) url.searchParams.set('start', String(Math.floor(start)));
  if (origin) url.searchParams.set('origin', origin);
  return url.toString();
}

export interface LectureSectionProps {
  lecture: LessonLecture;
  /** Heading level of the section title (the lesson title is the page h1). */
  headingLevel?: 2 | 3;
}

/**
 * The lesson's video lecture (pack v2).
 *
 * - YouTube (`youTubeId`): a click-to-play facade (i.ytimg.com thumbnail, maxres → hq fallback) so no YouTube code or
 *   cookie loads until the learner plays; then the privacy-enhanced youtube-nocookie.com embed. Chapter seek and playback
 *   speed go to the player with postMessage (IFrame API commands); the player's time updates highlight the current
 *   chapter here and in the transcript. Before playing, a chapter starts the player at that chapter (?start=).
 * - Self-hosted file (`src`): HTML5 player with poster and captions track, same chapters/speed/transcript.
 * - Not produced yet: a "coming soon" stage previewing each chapter's slide, the chapter list and the full transcript
 *   (also server-rendered for search engines).
 */
export function LectureSection({ lecture, headingLevel = 2 }: LectureSectionProps) {
  const Heading = `h${headingLevel}` as const;
  const titleId = useId();
  const transcriptId = useId();
  const videoRef = useRef<HTMLVideoElement>(null);
  const frameRef = useRef<HTMLIFrameElement>(null);
  const transcriptRef = useRef<HTMLDivElement>(null);
  const [active, setActive] = useState(0);
  const [chapterProgress, setChapterProgress] = useState(0);
  const [duration, setDuration] = useState<number | null>(null);
  const [speed, setSpeed] = useState<Speed>(1);
  const [transcriptOpen, setTranscriptOpen] = useState(false);
  const [ytStart, setYtStart] = useState<number | null>(null); // null = facade (the embed is not loaded)
  const [thumb, setThumb] = useState<'maxresdefault' | 'hqdefault' | 'none'>('maxresdefault');
  const youTubeId = lecture.produced ? lecture.youTubeId : null;
  const embedUrl = youTubeId ? (lecture.embedUrl ?? `${YOUTUBE_EMBED_ORIGIN}/embed/${youTubeId}`) : null;
  const fileSrc = lecture.produced && !youTubeId ? lecture.src : null;
  const produced = !!(embedUrl || fileSrc);
  const chapters = lecture.chapters;
  const planned = Math.max(1, lecture.totalSeconds);
  // Planned scene times → real video times (the produced video rarely matches the plan to the second).
  const scale = duration && Number.isFinite(duration) && duration > 0 ? duration / planned : 1;
  const startOf = useCallback((c: LectureChapter) => c.startSeconds * scale, [scale]);

  const onTime = useCallback(
    (t: number) => {
      let index = 0;
      for (let i = 0; i < chapters.length; i++) if (t >= startOf(chapters[i]!) - 0.25) index = i;
      setActive(index);
      const c = chapters[index]!;
      const len = Math.max(1, c.seconds * scale);
      setChapterProgress(Math.min(1, Math.max(0, (t - startOf(c)) / len)));
    },
    [chapters, scale, startOf],
  );

  // ---- YouTube: postMessage commands and time updates (the embed is loaded with enablejsapi=1).
  const ytCommand = useCallback((func: string, args: unknown[] = []) => {
    frameRef.current?.contentWindow?.postMessage(
      JSON.stringify({ event: 'command', func, args }),
      YOUTUBE_EMBED_ORIGIN,
    );
  }, []);

  useEffect(() => {
    if (!embedUrl || ytStart === null) return;
    const onMessage = (e: MessageEvent) => {
      if (
        e.origin !== YOUTUBE_EMBED_ORIGIN ||
        e.source !== frameRef.current?.contentWindow ||
        typeof e.data !== 'string'
      )
        return;
      let data: { event?: string; info?: { currentTime?: number; duration?: number } | null };
      try {
        data = JSON.parse(e.data) as typeof data;
      } catch {
        return;
      }
      if (data.event !== 'infoDelivery' || !data.info) return;
      if (typeof data.info.duration === 'number' && data.info.duration > 0) setDuration(data.info.duration);
      if (typeof data.info.currentTime === 'number') onTime(data.info.currentTime);
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [embedUrl, ytStart, onTime]);

  const onFrameLoad = () => {
    // Ask the player to stream its state (infoDelivery) to this window; then apply the chosen speed.
    frameRef.current?.contentWindow?.postMessage(
      JSON.stringify({ event: 'listening', id: titleId, channel: 'widget' }),
      YOUTUBE_EMBED_ORIGIN,
    );
    if (speed !== 1) ytCommand('setPlaybackRate', [speed]);
  };

  useEffect(() => {
    if (videoRef.current) videoRef.current.playbackRate = speed;
    if (embedUrl && ytStart !== null) ytCommand('setPlaybackRate', [speed]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [speed]);

  const openTranscriptAt = (index: number) => {
    setTranscriptOpen(true);
    // After the panel opens: bring the chapter's transcript into view (no smooth scroll with reduced motion: base.css).
    window.setTimeout(() => {
      transcriptRef.current
        ?.querySelector<HTMLElement>(`[data-chapter="${index}"]`)
        ?.scrollIntoView?.({ block: 'nearest' });
    }, 50);
  };

  const selectChapter = (index: number) => {
    setActive(index);
    setChapterProgress(0);
    const at = startOf(chapters[index]!);
    if (embedUrl) {
      if (ytStart === null)
        setYtStart(at); // loads the player at the chapter
      else {
        ytCommand('seekTo', [at, true]);
        ytCommand('playVideo');
      }
      return;
    }
    const v = videoRef.current;
    if (fileSrc && v) {
      v.currentTime = at;
      void v.play?.()?.catch?.(() => undefined);
    }
  };

  const current = chapters[active];
  const origin = typeof window !== 'undefined' ? window.location.origin : '';

  return (
    <section className={clsx('lx-lecture', produced ? 'is-produced' : 'is-soon')} aria-labelledby={titleId}>
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
            {lecture.captions || youTubeId ? ' · captions' : ''} · full transcript
          </p>
        </div>
        <span className={clsx('lx-lecture__status', produced ? 'is-live' : 'is-soon')}>
          {produced ? (
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

      {embedUrl && youTubeId ? (
        <div className="lx-lecture__stage lx-yt">
          {ytStart === null ? (
            <button
              type="button"
              className="lx-yt__facade"
              onClick={() => setYtStart(0)}
              aria-label={`Play the video lecture: ${lecture.title}`}
            >
              {thumb !== 'none' && (
                <img
                  src={lecture.poster ?? youTubeThumbnail(youTubeId, thumb)}
                  alt=""
                  width={1280}
                  height={720}
                  loading="lazy"
                  decoding="async"
                  // maxresdefault is missing for non-HD uploads (404, or a small grey placeholder).
                  // hqdefault failing too (offline, blocked): show the branded stage instead of a broken image.
                  onError={() =>
                    setThumb((t) => (t === 'maxresdefault' && !lecture.poster ? 'hqdefault' : 'none'))
                  }
                  onLoad={(e) => {
                    if (
                      !lecture.poster &&
                      thumb === 'maxresdefault' &&
                      e.currentTarget.naturalWidth > 0 &&
                      e.currentTarget.naturalWidth < 200
                    )
                      setThumb('hqdefault');
                  }}
                />
              )}
              <span className="lx-yt__play" aria-hidden="true">
                <Play />
              </span>
              <span className="lx-yt__note" aria-hidden="true">
                Plays from YouTube (privacy-enhanced mode)
              </span>
            </button>
          ) : (
            <iframe
              ref={frameRef}
              className="lx-yt__frame"
              src={youTubeEmbedSrc(embedUrl, ytStart, origin)}
              title={`Video lecture: ${lecture.title}`}
              allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
              allowFullScreen
              referrerPolicy="strict-origin-when-cross-origin"
              onLoad={onFrameLoad}
            />
          )}
        </div>
      ) : fileSrc ? (
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
            onTimeUpdate={(e) => onTime(e.currentTarget.currentTime)}
            aria-describedby={transcriptId}
          >
            <source src={fileSrc} type="video/mp4" />
            {lecture.captions && (
              <track kind="captions" src={lecture.captions} srcLang="en" label="English" default />
            )}
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
            <p className="lx-lecture__slide-title" key={`t${active}`}>
              {current?.title}
            </p>
            {current && current.points.length > 0 && (
              <ul className="lx-lecture__slide-points" key={`p${active}`}>
                {current.points.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            )}
            <div className="lx-lecture__slide-dots" aria-hidden="true">
              {chapters.map((c) => (
                <span
                  key={c.index}
                  className={clsx(c.index === active && 'is-on', c.index < active && 'is-past')}
                />
              ))}
            </div>
          </div>
          <div className="lx-lecture__soon-text">
            <p className="lx-lecture__soon-title">
              <Headphones aria-hidden="true" /> The narrated lecture is in production
            </p>
            <p className="lx-muted">
              Every chapter is scripted and ready. Browse the chapters and read the full transcript now — the
              video will appear here when it’s published.
            </p>
          </div>
        </div>
      )}

      <div className="lx-lecture__controls">
        {produced && (
          <div className="lx-seg" role="radiogroup" aria-label="Playback speed">
            <span
              className="lx-seg__thumb"
              aria-hidden="true"
              style={
                {
                  ['--seg-i' as string]: SPEEDS.indexOf(speed),
                  ['--seg-n' as string]: SPEEDS.length,
                } as CSSProperties
              }
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
              <li
                key={c.index}
                className={clsx('lx-chapter', isActive && 'is-active', c.index < active && 'is-past')}
              >
                <button
                  type="button"
                  className="lx-chapter__btn"
                  aria-current={isActive ? 'step' : undefined}
                  onClick={() => selectChapter(c.index)}
                >
                  <span className="lx-chapter__time">{formatClock(startOf(c))}</span>
                  <span className="lx-chapter__title">{c.title}</span>
                  <span className="lx-chapter__bar" aria-hidden="true">
                    <span
                      style={{
                        transform: `scaleX(${isActive ? (produced ? chapterProgress : 1) : c.index < active ? 1 : 0})`,
                      }}
                    />
                  </span>
                </button>
                {!produced && (
                  <button
                    type="button"
                    className="lx-chapter__read"
                    onClick={() => openTranscriptAt(c.index)}
                  >
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
            <Captions aria-hidden="true" /> Transcript of the narration, chapter by chapter
            {produced ? ' — the chapter playing now is highlighted.' : '.'}
          </p>
          {chapters.map((c) => (
            <section
              key={c.index}
              className={clsx('lx-transcript__chapter', produced && c.index === active && 'is-current')}
              data-chapter={c.index}
              aria-label={c.title}
              aria-current={produced && c.index === active ? 'true' : undefined}
            >
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
