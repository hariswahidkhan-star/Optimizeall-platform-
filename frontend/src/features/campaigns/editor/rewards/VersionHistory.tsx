import { History } from 'lucide-react';
import { Alert, Badge, Card, CardBody, CardHeader, EmptyState, Skeleton, Timeline } from '@/components/ui';
import { pluralize } from '@/lib/format/text';
import type { RewardRuleSet } from '../../api/types';

/** Timeline of immutable reward rule versions, newest first. */
export function VersionHistory({ versions, loading }: { versions: RewardRuleSet[]; loading?: boolean }) {
  return (
    <Card as="section" aria-labelledby="reward-versions-title">
      <CardHeader titleId="reward-versions-title" headingLevel={3} title="Version history" />
      <CardBody className="stack">
        <Alert tone="info" icon={<History />}>
          Saving rules always creates a new version. Existing submissions keep the version that was in force
          when they were submitted, and approved earnings are never changed.
        </Alert>
        {loading ? (
          <Skeleton height={80} />
        ) : versions.length === 0 ? (
          <EmptyState compact headingLevel={4} title="No versions yet" />
        ) : (
          <Timeline
            label="Reward rule versions"
            items={versions.map((v) => ({
              id: v.id,
              timestamp: v.effectiveFrom,
              tone: v.isCurrent ? 'success' : 'neutral',
              actor: v.createdBy?.displayName,
              title: (
                <span className="cluster mg-cluster-sm">
                  <span>Version {v.version}</span>
                  {v.isCurrent && <Badge tone="success">Current</Badge>}
                  <Badge tone="neutral">{pluralize(v.inUseBySubmissions, 'submission')} priced with it</Badge>
                </span>
              ),
              description: (
                <span className="stack mg-stack-xs">
                  <span>{v.summary}</span>
                  {v.reason && <span className="text-muted">Reason: {v.reason}</span>}
                </span>
              ),
            }))}
          />
        )}
      </CardBody>
    </Card>
  );
}
