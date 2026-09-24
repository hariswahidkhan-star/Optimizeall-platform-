import type { SelectOption } from '@/components/ui';
import { SOCIAL_PLATFORMS } from '@/features/campaigns/api/types';
import { CONTENT_FORMATS, type ContentFormat, type RateSourceLevel } from './api/types';

export const FORMAT_LABELS: Record<ContentFormat, string> = {
  Post: 'Feed post',
  Story: 'Story',
  ShortVideo: 'Reel / Short',
  LongVideo: 'Long video',
  Carousel: 'Carousel',
};

export const formatOptions: SelectOption[] = CONTENT_FORMATS.map((f) => ({
  value: f,
  label: FORMAT_LABELS[f],
}));

export const platformOptions: SelectOption[] = SOCIAL_PLATFORMS.map((p) => ({
  value: p,
  label: p === 'X' ? 'X (Twitter)' : p,
}));

export function formatLabel(format: string | null | undefined): string {
  return format ? (FORMAT_LABELS[format as ContentFormat] ?? format) : 'Any format';
}

export const LEVEL_LABELS: Record<RateSourceLevel, string> = {
  CampaignPersonalCustom: 'Custom rate · this campaign',
  CampaignPersonalCard: 'Personal card · this campaign',
  CampaignGroup: 'Group · this campaign',
  GlobalPersonalCustom: 'Custom rate · all campaigns',
  GlobalPersonalCard: 'Personal card · all campaigns',
  GlobalGroup: 'Group · all campaigns',
  CampaignSegment: 'Automatic segment · this campaign',
  GlobalSegment: 'Automatic segment · all campaigns',
  CampaignRules: 'Campaign rules',
};

export function levelLabel(level: string): string {
  return LEVEL_LABELS[level as RateSourceLevel] ?? level;
}

export function levelTone(level: string): 'brand' | 'info' | 'success' | 'neutral' | 'warning' {
  if (level.includes('Custom')) return 'brand';
  if (level.includes('PersonalCard')) return 'info';
  if (level.includes('Group')) return 'success';
  if (level.includes('Segment')) return 'warning';
  return 'neutral';
}

/** "Instagram · Reel / Short · PK" style description of a rate line's conditions. */
export function lineConditions(line: {
  platform: string | null;
  format: string | null;
  countryCode: string | null;
}): string {
  const parts: string[] = [];
  if (line.platform) parts.push(line.platform === 'X' ? 'X (Twitter)' : line.platform);
  if (line.format) parts.push(formatLabel(line.format));
  if (line.countryCode) parts.push(line.countryCode);
  return parts.length === 0 ? 'Any post (default)' : parts.join(' · ');
}

/** Datetime-local input value (UTC) ↔ ISO. The rates screens enter windows in UTC to stay unambiguous. */
export function isoToUtcInput(iso: string | null | undefined): string {
  if (!iso) return '';
  return iso.slice(0, 16);
}

export function utcInputToIso(value: string): string | null {
  if (!value.trim()) return null;
  return `${value}:00Z`;
}
