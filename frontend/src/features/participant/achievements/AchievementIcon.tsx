import {
  Award,
  BadgeCheck,
  Coins,
  Crown,
  Flame,
  Gift,
  Heart,
  Medal,
  Rocket,
  Share2,
  Sparkles,
  Star,
  Target,
  ThumbsUp,
  Trophy,
  Users,
  Wallet,
  Zap,
  type LucideIcon,
} from 'lucide-react';

/** Achievement icons arrive as lucide names ("badge-check"); unknown names fall back to a medal. */
const ICONS: Record<string, LucideIcon> = {
  award: Award,
  'badge-check': BadgeCheck,
  coins: Coins,
  crown: Crown,
  flame: Flame,
  gift: Gift,
  heart: Heart,
  medal: Medal,
  rocket: Rocket,
  'share-2': Share2,
  share: Share2,
  sparkles: Sparkles,
  star: Star,
  target: Target,
  'thumbs-up': ThumbsUp,
  trophy: Trophy,
  users: Users,
  wallet: Wallet,
  zap: Zap,
};

export function AchievementIcon({ name, size = 22 }: { name: string | null; size?: number }) {
  const Icon = (name && ICONS[name.toLowerCase()]) || Medal;
  return <Icon aria-hidden="true" size={size} />;
}
