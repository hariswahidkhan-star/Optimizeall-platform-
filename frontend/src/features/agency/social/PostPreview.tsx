import { Film } from 'lucide-react';
import { ProtectedImage } from '@/components/ProtectedImage';
import { isSafeHref } from '@/lib/safeHref';
import type { Media, Preset, SocialNetwork } from './api';
import { NETWORK_LABELS } from './api';
import { countFor } from './counters';

/** Private uploads need the session; public files and external https URLs load directly. */
export function MediaImage({ media, alt, className }: { media: Media; alt: string; className?: string }) {
  if (media.kind === 'Video')
    return (
      <span className={`sm-preview__placeholder ${className ?? ''}`} role="img" aria-label={`Video: ${alt}`}>
        <Film aria-hidden="true" /> {media.title}
      </span>
    );
  if (media.previewUrl.startsWith('/api/v1/agency/') || media.previewUrl.startsWith('/api/v1/client/'))
    return <ProtectedImage src={media.previewUrl} alt={alt} className={className} />;
  if (!isSafeHref(media.previewUrl)) return <span className={className}>{alt}</span>;
  return <img src={media.previewUrl} alt={alt} className={className} loading="lazy" />;
}

export interface PreviewProps {
  network: SocialNetwork;
  displayName: string;
  handle: string;
  text: string;
  title?: string | null;
  hashtags: string[];
  link?: string | null;
  media: Media[];
  altTexts: string[];
  preset: Preset | undefined;
}

/**
 * A mock-up of how the post will look on the network (layout only, not pixel-exact), with the live character counter.
 * The text shown is the composed text (hashtags and, where the network puts links in the text, the link).
 */
export function PostPreview({ network, displayName, handle, text, title, hashtags, link, media, altTexts, preset }: PreviewProps) {
  const counter = preset ? countFor(preset, text, hashtags, link) : null;
  const initials = displayName.slice(0, 1).toUpperCase() || '?';
  const shownMedia = network === 'X' ? media.slice(0, 4) : network === 'GoogleBusiness' ? media.slice(0, 1) : media;
  const vertical = network === 'TikTok' || network === 'YouTube';
  const mediaBlock =
    shownMedia.length > 0 ? (
      <div className="sm-preview__media">
        {shownMedia.map((m, i) => (
          <MediaImage key={m.id} media={m} alt={altTexts[i] || m.altText || m.title} />
        ))}
      </div>
    ) : vertical ? (
      <div className="sm-preview__placeholder">No video selected</div>
    ) : null;

  const body = (
    <p className="sm-preview__text">
      {network === 'YouTube' && title ? (
        <>
          <strong>{title}</strong>
          {'\n'}
        </>
      ) : null}
      {counter?.finalText ?? text}
    </p>
  );

  return (
    <figure className={`sm-preview sm-preview--${network.toLowerCase()}`} aria-label={`${NETWORK_LABELS[network]} preview`} style={{ margin: 0 }}>
      <div className="sm-preview__head">
        <span className="sm-preview__avatar" aria-hidden="true">
          {initials}
        </span>
        <span className="stack" style={{ gap: 0 }}>
          <span className="sm-preview__name">{displayName}</span>
          <span className="sm-muted">@{handle}</span>
        </span>
      </div>
      {network === 'Instagram' || vertical ? (
        <>
          {mediaBlock}
          {body}
        </>
      ) : (
        <>
          {body}
          {mediaBlock}
        </>
      )}
      {link && preset && preset.linkHandling !== 'InText' && (
        <p className="sm-preview__link">
          {preset.linkHandling === 'NotClickable' ? 'Link in bio: ' : ''}
          {link}
        </p>
      )}
      {counter && (
        <figcaption className={`sm-counter ${counter.over ? 'sm-counter--over' : ''}`} style={{ padding: '0 0.75rem 0.75rem' }}>
          {counter.length.toLocaleString()} / {counter.max.toLocaleString()} characters
          {counter.over ? ` — ${(-counter.remaining).toLocaleString()} over the limit` : ''}
        </figcaption>
      )}
    </figure>
  );
}
