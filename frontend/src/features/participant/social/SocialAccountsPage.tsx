import { useMutation, useQueryClient } from '@tanstack/react-query';
import { BadgeCheck, CircleCheck, CircleSlash, Pencil, Plus, Power, RotateCcw, Share2 } from 'lucide-react';
import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Card, CardBody } from '@/components/ui/Card';
import { ConfirmDialog } from '@/components/ui/ConfirmDialog';
import { DateTime } from '@/components/ui/DateTime';
import { EmptyState } from '@/components/ui/EmptyState';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useToast } from '@/components/ui/toastContext';
import { countryName } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { formatNumber } from '@/lib/format/money';
import { pluralize } from '@/lib/format/text';
import { qk, useSocialAccounts } from '../api/queries';
import type { SocialAccount, SocialAccountList } from '../api/types';
import { PlatformTag } from '../components/Platform';
import { QueryState } from '../components/QueryState';
import { daysUntil } from '../lib/zonedTime';
import { SocialAccountDialog } from './SocialAccountDialog';
import '../participant.css';
import { SafeExternalLink } from '@/components/SafeExternalLink';

type Action = 'deactivate' | 'reactivate' | 'request-verification';

function useAccountAction() {
  const client = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: ({ account, action }: { account: SocialAccount; action: Action }) => {
      if (action === 'deactivate') return api.delete<SocialAccount>(`/me/social-accounts/${account.id}`);
      return api.post<SocialAccount>(`/me/social-accounts/${account.id}/${action}`);
    },
    onSuccess: (_data, { account, action }) => {
      const messages: Record<Action, string> = {
        deactivate: 'Profile deactivated',
        reactivate: 'Profile reactivated',
        'request-verification': 'Verification requested',
      };
      toast.success(messages[action], `@${account.handle}`);
      void client.invalidateQueries({ queryKey: qk.socialAccounts });
      void client.invalidateQueries({ queryKey: qk.home });
      void client.invalidateQueries({ queryKey: qk.campaigns });
    },
    onError: (error) => toast.error('That didn’t work', errorMessage(error)),
  });
}

function AccountCard({
  account,
  onEdit,
  onDeactivate,
  action,
}: {
  account: SocialAccount;
  onEdit: () => void;
  onDeactivate: () => void;
  action: ReturnType<typeof useAccountAction>;
}) {
  const busy = action.isPending && action.variables?.account.id === account.id;
  const canRequest =
    account.isActive &&
    (account.verificationStatus === 'Unverified' || account.verificationStatus === 'Rejected');
  const daysLeft = account.eligibleFrom ? daysUntil(account.eligibleFrom) : 0;
  const titleId = `social-${account.id}-title`;

  return (
    <Card as="article" aria-labelledby={titleId} flat={!account.isActive}>
      <CardBody className="stack">
        <div className="pp-section__head">
          <div className="cluster" style={{ ['--cluster-gap' as string]: 'var(--space-2)' }}>
            <PlatformTag platform={account.platform} />
            <h2 id={titleId} className="pp-list__title" style={{ fontSize: 'var(--text-h4)' }}>
              <SafeExternalLink
                href={account.profileUrl}
                className="ui-link"
                nofollow
                fallback={<>@{account.handle}</>}
              >
                @{account.handle}
                <span className="visually-hidden"> (opens profile in a new tab)</span>
              </SafeExternalLink>
            </h2>
          </div>
          <div className="cluster" style={{ ['--cluster-gap' as string]: 'var(--space-2)' }}>
            {!account.isActive ? (
              <Badge tone="neutral">Deactivated</Badge>
            ) : account.qualifies ? (
              <Badge tone="success" icon={<CircleCheck size={14} />}>
                Qualifies
              </Badge>
            ) : (
              <Badge tone="warning" icon={<CircleSlash size={14} />}>
                Doesn’t qualify yet
              </Badge>
            )}
            <StatusBadge kind="socialVerification" status={account.verificationStatus} />
          </div>
        </div>

        {account.isActive && !account.qualifies && account.reasons.length > 0 && (
          <ul className="text-small" style={{ paddingLeft: 'var(--space-5)' }}>
            {account.reasons.map((r) => (
              <li key={r.code}>{r.message}</li>
            ))}
          </ul>
        )}
        {account.isActive && account.eligibleFrom && daysLeft > 0 && (
          <p className="text-small">
            <strong>{pluralize(daysLeft, 'day')}</strong> until it qualifies (
            <DateTime value={account.eligibleFrom} format="date" />
            ).
          </p>
        )}
        {account.verificationStatus === 'Rejected' && account.verificationNote && (
          <Alert tone="danger" title="Verification was rejected">
            {account.verificationNote}
          </Alert>
        )}

        <KeyValueList
          items={[
            {
              label: 'Account created',
              value: (
                <>
                  <DateTime value={account.accountCreatedAt} format="date" timeZone="UTC" /> (
                  {pluralize(account.accountAgeDays, 'day')} old)
                </>
              ),
            },
            {
              label: 'Followers',
              value: <span className="tabular">{formatNumber(account.followerCount)}</span>,
            },
            { label: 'Language', value: account.primaryLanguage ?? '—' },
            {
              label: 'Audience country',
              value: account.audienceCountryCode ? countryName(account.audienceCountryCode) : '—',
            },
          ]}
        />

        <div className="pp-actions">
          {account.isActive ? (
            <>
              <Button size="sm" variant="secondary" leadingIcon={<Pencil />} onClick={onEdit} disabled={busy}>
                Edit
              </Button>
              {canRequest && (
                <Button
                  size="sm"
                  variant="secondary"
                  leadingIcon={<BadgeCheck />}
                  loading={busy && action.variables?.action === 'request-verification'}
                  disabled={busy && action.variables?.action !== 'request-verification'}
                  onClick={() => action.mutate({ account, action: 'request-verification' })}
                >
                  Request verification
                </Button>
              )}
              <Button
                size="sm"
                variant="ghost"
                leadingIcon={<Power />}
                onClick={onDeactivate}
                disabled={busy}
              >
                Deactivate
              </Button>
            </>
          ) : (
            <Button
              size="sm"
              variant="secondary"
              leadingIcon={<RotateCcw />}
              loading={busy}
              onClick={() => action.mutate({ account, action: 'reactivate' })}
            >
              Reactivate
            </Button>
          )}
        </div>
      </CardBody>
    </Card>
  );
}

function SocialAccountsView({ data }: { data: SocialAccountList }) {
  const [params, setParams] = useSearchParams();
  const [editing, setEditing] = useState<SocialAccount | null>(null);
  const [deactivating, setDeactivating] = useState<SocialAccount | null>(null);
  const [savedMessage, setSavedMessage] = useState<string | null>(null);
  const action = useAccountAction();
  const adding = params.get('add') === '1';
  const activeCount = data.items.filter((a) => a.isActive).length;
  const atLimit = activeCount >= data.maxActiveAccounts;

  const openAdd = () => setParams({ add: '1' }, { replace: true });
  const closeAdd = () => setParams({}, { replace: true });

  return (
    <div className="pp-page">
      <PageHeader
        title="Social accounts"
        description="The established profiles you share campaign content from."
        actions={
          <Button leadingIcon={<Plus />} onClick={openAdd} disabled={atLimit}>
            Add a profile
          </Button>
        }
      />

      <Alert tone="info" title="How profiles qualify">
        <ul>
          <li>
            A profile must be at least <strong>{pluralize(data.minAccountAgeDays, 'day')} old</strong>,
            counted from the date it was created on the platform.
          </li>
          {data.minFollowers > 0 && (
            <li>
              It needs at least <strong>{formatNumber(data.minFollowers)} followers</strong>.
            </li>
          )}
          <li>
            You can have up to <strong>{data.maxActiveAccounts} active profiles</strong> ({activeCount} in
            use). Some campaigns also require a verified profile.
          </li>
        </ul>
      </Alert>

      <p className="visually-hidden" role="status" aria-live="polite">
        {savedMessage ?? ''}
      </p>
      {savedMessage && (
        <Alert tone="warning" onDismiss={() => setSavedMessage(null)}>
          {savedMessage}
        </Alert>
      )}

      {data.items.length === 0 ? (
        <Card flat>
          <EmptyState
            icon={<Share2 />}
            title="No profiles yet"
            description="Add the Instagram, TikTok, YouTube or other profile you’ll post from."
            action={
              <Button leadingIcon={<Plus />} onClick={openAdd}>
                Add a profile
              </Button>
            }
          />
        </Card>
      ) : (
        <div className="pp-grid" style={{ ['--pp-grid-min' as string]: '320px' }}>
          {data.items.map((account) => (
            <AccountCard
              key={account.id}
              account={account}
              action={action}
              onEdit={() => setEditing(account)}
              onDeactivate={() => setDeactivating(account)}
            />
          ))}
        </div>
      )}

      {(adding || editing) && (
        <SocialAccountDialog
          open
          account={editing}
          minAccountAgeDays={data.minAccountAgeDays}
          onClose={() => (editing ? setEditing(null) : closeAdd())}
          onSaved={(message, reset) => setSavedMessage(reset ? message : null)}
        />
      )}

      <ConfirmDialog
        open={!!deactivating}
        onClose={() => setDeactivating(null)}
        title={`Deactivate @${deactivating?.handle ?? ''}?`}
        description="You won’t be able to submit posts from this profile until you reactivate it. Past submissions are kept."
        confirmLabel="Deactivate"
        tone="danger"
        onConfirm={async () => {
          if (!deactivating) return;
          await action.mutateAsync({ account: deactivating, action: 'deactivate' });
          setDeactivating(null);
        }}
      />
    </div>
  );
}

export function SocialAccountsPage() {
  const query = useSocialAccounts();
  return (
    <QueryState query={query} errorTitle="Your social profiles couldn’t be loaded">
      {(data) => <SocialAccountsView data={data} />}
    </QueryState>
  );
}
