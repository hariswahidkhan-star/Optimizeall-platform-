import {
  AtSign,
  BriefcaseBusiness,
  Camera,
  Ghost,
  MessageCircle,
  Music2,
  Pin,
  Play,
  Share2,
  Users,
  type LucideIcon,
} from 'lucide-react';

/**
 * Platform glyphs. Brand logos are trademarks (and not part of the icon set), so each platform gets a neutral
 * pictogram that is always paired with its name.
 */
const ICONS: Record<string, LucideIcon> = {
  Instagram: Camera,
  TikTok: Music2,
  X: AtSign,
  Facebook: Users,
  LinkedIn: BriefcaseBusiness,
  YouTube: Play,
  Threads: MessageCircle,
  Pinterest: Pin,
  Snapchat: Ghost,
};

export function platformLabel(platform: string): string {
  return platform === 'X' ? 'X (Twitter)' : platform;
}

export function PlatformIcon({ platform, className }: { platform: string; className?: string }) {
  const Icon = ICONS[platform] ?? Share2;
  return <Icon aria-hidden="true" className={className} />;
}

/** Platform chip: icon + visible name. */
export function PlatformTag({ platform }: { platform: string }) {
  return (
    <span className="pp-platform">
      <PlatformIcon platform={platform} />
      <span>{platformLabel(platform)}</span>
    </span>
  );
}

export function PlatformList({ platforms, label = 'Platforms' }: { platforms: string[]; label?: string }) {
  return (
    <ul className="pp-platforms" aria-label={label}>
      {platforms.map((p) => (
        <li key={p}>
          <PlatformTag platform={p} />
        </li>
      ))}
    </ul>
  );
}
