import { useId } from 'react';
import { useLocation } from 'react-router-dom';
import catalog from './siteVideos.json';
import { Markdown } from './Markdown';

/**
 * A self-hosted website video (docs/SEO_CRO.md § Video): files in /media/videos/ (MP4 + WebM + poster + WebVTT
 * captions). The player is lazy (`preload="none"`: nothing downloads before play), reserves its 16:9 box (no layout
 * shift), has native keyboard-accessible controls, a captions track on by default, a download fallback and an optional
 * transcript. Its VideoObject JSON-LD and video-sitemap entry come from the server (SeoPageResolver).
 */
export interface SiteVideoData {
  title: string;
  description?: string | null;
  mp4Url?: string | null;
  webmUrl?: string | null;
  posterUrl?: string | null;
  captionsUrl?: string | null;
  captionsLanguage?: string | null;
  durationSeconds?: number | null;
  transcript?: string | null;
}

function languageLabel(code: string): string {
  try {
    return new Intl.DisplayNames(['en'], { type: 'language' }).of(code) ?? code;
  } catch {
    return code;
  }
}

export function SiteVideo({ video, headingLevel = 2 }: { video: SiteVideoData; headingLevel?: 2 | 3 | null }) {
  const id = useId();
  const src = video.mp4Url || video.webmUrl;
  // Captions are required (WCAG 1.2.2): a video without a captions file is not shown.
  if (!src || !video.captionsUrl) return null;
  const Heading = headingLevel === 3 ? 'h3' : 'h2';
  const lang = video.captionsLanguage || 'en';
  return (
    <figure className="site-video" aria-labelledby={headingLevel ? `${id}-title` : undefined}>
      {headingLevel && (
        <Heading id={`${id}-title`} className="site-section__title">
          {video.title}
        </Heading>
      )}
      <div className="site-video__frame">
        <video
          className="site-video__player"
          controls
          preload="none"
          playsInline
          width={1280}
          height={720}
          poster={video.posterUrl ?? undefined}
          aria-label={video.title}
          aria-describedby={video.description ? `${id}-desc` : undefined}
        >
          {video.webmUrl && <source src={video.webmUrl} type="video/webm" />}
          {video.mp4Url && <source src={video.mp4Url} type="video/mp4" />}
          <track kind="captions" src={video.captionsUrl} srcLang={lang} label={languageLabel(lang)} default />
          <a href={src}>Download the video: {video.title}</a>
        </video>
      </div>
      {video.description && (
        <figcaption id={`${id}-desc`} className="site-video__caption">
          {video.description}
        </figcaption>
      )}
      {video.transcript && (
        <details className="site-video__transcript">
          <summary>Transcript</summary>
          <Markdown source={video.transcript} />
        </details>
      )}
    </figure>
  );
}

interface CatalogVideo extends SiteVideoData {
  path: string;
}

/** Videos placed on built-in pages in code (siteVideos.json, identical to the backend's site-videos.json). */
export const SITE_VIDEOS = (catalog as { videos: CatalogVideo[] }).videos;

/** Renders the catalog videos for the current path (none by default). Placed at the end of each public page. */
export function PageVideos() {
  const { pathname } = useLocation();
  const videos = SITE_VIDEOS.filter((v) => v.path === pathname);
  if (videos.length === 0) return null;
  return (
    <div className="site-section">
      <div className="container site-narrow">
        {videos.map((v) => (
          <SiteVideo key={`${v.path}-${v.title}`} video={v} />
        ))}
      </div>
    </div>
  );
}
