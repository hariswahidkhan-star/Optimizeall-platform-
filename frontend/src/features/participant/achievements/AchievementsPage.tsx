import { Award } from 'lucide-react';
import { Badge } from '@/components/ui/Badge';
import { Card } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { ProgressBar, ProgressRing } from '@/components/ui/Progress';
import { formatNumber } from '@/lib/format/money';
import { useAchievements } from '../api/queries';
import type { Achievement } from '../api/types';
import { QueryState } from '../components/QueryState';
import { AchievementIcon } from './AchievementIcon';
import '../participant.css';

function progressText(a: Achievement) {
  const value = Math.min(a.progress, a.threshold);
  return `${formatNumber(value)} of ${formatNumber(a.threshold)}`;
}

function AchievementCard({ a }: { a: Achievement }) {
  const awarded = !!a.awardedAt;
  return (
    <Card as="li" className="pp-achievement" data-awarded={awarded}>
      <ProgressRing
        value={Math.min(a.progress, a.threshold)}
        max={a.threshold}
        label={`${a.name} progress`}
        size={84}
        centerText={
          <span className="pp-achievement__icon" style={{ width: 40, height: 40 }}>
            <AchievementIcon name={a.icon} size={20} />
          </span>
        }
      />
      <h2 className="pp-achievement__name">{a.name}</h2>
      <p className="text-small pp-muted">{a.description}</p>
      {awarded ? (
        <Badge tone="success">
          Earned <DateTime value={a.awardedAt} format="date" />
        </Badge>
      ) : (
        <ProgressBar
          value={Math.min(a.progress, a.threshold)}
          max={a.threshold}
          label="Progress"
          valueText={progressText(a)}
          tone="accent"
          className="pp-achievement__bar"
        />
      )}
    </Card>
  );
}

export function AchievementsPage() {
  const query = useAchievements();
  return (
    <div className="pp-page">
      <PageHeader
        title="Achievements"
        description="Milestones you reach as you share campaigns and invite friends."
      />
      <QueryState query={query} errorTitle="Achievements couldn’t be loaded">
        {(items) => {
          if (items.length === 0)
            return (
              <Card flat>
                <EmptyState
                  icon={<Award />}
                  title="No achievements yet"
                  description="Milestones will appear here soon."
                />
              </Card>
            );
          const awarded = items.filter((a) => a.awardedAt);
          const sorted = [
            ...awarded.sort((a, b) => (b.awardedAt ?? '').localeCompare(a.awardedAt ?? '')),
            ...items.filter((a) => !a.awardedAt),
          ];
          return (
            <>
              <p className="pp-muted" role="status">
                {awarded.length} of {items.length} earned
              </p>
              <ul
                className="pp-grid"
                style={{ listStyle: 'none', padding: 0, margin: 0, ['--pp-grid-min' as string]: '220px' }}
              >
                {sorted.map((a) => (
                  <AchievementCard key={a.key} a={a} />
                ))}
              </ul>
            </>
          );
        }}
      </QueryState>
    </div>
  );
}
