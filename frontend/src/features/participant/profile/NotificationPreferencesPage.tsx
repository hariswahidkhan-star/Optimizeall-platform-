import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Lock } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { Checkbox } from '@/components/ui/Checkbox';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { pluralize } from '@/lib/format/text';
import { qk, useNotificationPreferences } from '../api/queries';
import type { NotificationChannel, NotificationPreferences } from '../api/types';
import { QueryState } from '../components/QueryState';
import '../participant.css';

/** Channels the participant can choose (in-app is always on). */
const CHANNELS: { id: NotificationChannel; label: string }[] = [
  { id: 'Email', label: 'Email' },
  { id: 'WhatsApp', label: 'WhatsApp' },
];

const key = (type: string, channel: string) => `${type}::${channel}`;

function PreferencesMatrix({ prefs }: { prefs: NotificationPreferences }) {
  const toast = useToast();
  const client = useQueryClient();
  const [draft, setDraft] = useState<Record<string, boolean>>({});

  const original = useMemo(() => {
    const map: Record<string, boolean> = {};
    for (const row of prefs.types)
      for (const cell of row.channels) map[key(row.type, cell.channel)] = cell.enabled;
    return map;
  }, [prefs]);

  const changes = Object.entries(draft).filter(([k, v]) => original[k] !== v);

  const save = useMutation({
    mutationFn: () =>
      api.put<NotificationPreferences>('/me/notification-preferences', {
        preferences: changes.map(([k, enabled]) => {
          const [type, channel] = k.split('::');
          return { type, channel, enabled };
        }),
      }),
    onSuccess: (updated) => {
      client.setQueryData(qk.notificationPreferences, updated);
      setDraft({});
      toast.success('Notification preferences saved');
    },
    onError: (error) => toast.error('Preferences not saved', errorMessage(error)),
  });

  const whatsApp = prefs.channels.find((c) => c.channel === 'WhatsApp');

  return (
    <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-5)' }}>
      {whatsApp && !whatsApp.available && (
        <Alert tone="neutral" title="WhatsApp isn’t available">
          {whatsApp.reason ?? 'WhatsApp notifications are not available.'}{' '}
          <Link to="/app/profile" className="ui-link">
            Add your WhatsApp number in your profile
          </Link>
          .
        </Alert>
      )}
      <Card as="section" aria-labelledby="prefs-title">
        <CardHeader
          titleId="prefs-title"
          title="What we send you"
          description="In-app notifications are always on. Essential account and payout messages can’t be turned off."
        />
        <CardBody>
          <table className="pp-matrix">
            <caption className="visually-hidden">Notification preferences by type and channel</caption>
            <thead>
              <tr>
                <th scope="col">Notification</th>
                {CHANNELS.map((c) => (
                  <th key={c.id} scope="col" data-align="center">
                    {c.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {/* The API also lists staff-only types (labelled "(staff)"); they never reach participants. */}
              {prefs.types
                .filter((row) => !/\(staff\)\s*$/i.test(row.label))
                .map((row) => (
                  <tr key={row.type}>
                    <th scope="row">
                      <span className="pp-matrix__type">
                        <span className="pp-matrix__label">{row.label}</span>
                        <span className="pp-matrix__desc">{row.description}</span>
                        {row.marketing && (
                          <span className="pp-matrix__desc">
                            Email also needs marketing emails turned on in your profile.
                          </span>
                        )}
                      </span>
                    </th>
                    {CHANNELS.map((c) => {
                      const cell = row.channels.find((x) => x.channel === c.id);
                      if (!cell) return <td key={c.id} data-align="center" data-channel={c.label} />;
                      const k = key(row.type, c.id);
                      const checked = draft[k] ?? cell.enabled;
                      const label = `${c.label} for ${row.label}`;
                      if (cell.locked) {
                        return (
                          <td key={c.id} data-align="center" data-channel={c.label}>
                            <span className="pp-lock">
                              <Lock aria-hidden="true" />
                              <span>
                                {!cell.available ? 'Unavailable' : cell.enabled ? 'Always on' : 'Off'}
                              </span>
                              <span className="visually-hidden">
                                : {label} is essential and can’t be changed
                              </span>
                            </span>
                          </td>
                        );
                      }
                      return (
                        <td key={c.id} data-align="center" data-channel={c.label}>
                          <Checkbox
                            label={
                              <span className="visually-hidden">
                                {cell.available
                                  ? label
                                  : `${label} (unavailable${whatsApp?.reason ? `: ${whatsApp.reason}` : ''})`}
                              </span>
                            }
                            checked={checked && cell.available}
                            disabled={!cell.available || save.isPending}
                            onChange={(e) => setDraft((d) => ({ ...d, [k]: e.target.checked }))}
                          />
                        </td>
                      );
                    })}
                  </tr>
                ))}
            </tbody>
          </table>
        </CardBody>
      </Card>
      <div className="pp-actions">
        <Button onClick={() => save.mutate()} loading={save.isPending} disabled={changes.length === 0}>
          Save preferences
        </Button>
        {changes.length > 0 && (
          <>
            <Button variant="ghost" onClick={() => setDraft({})} disabled={save.isPending}>
              Discard changes
            </Button>
            <span className="text-small pp-muted" role="status">
              {pluralize(changes.length, 'unsaved change')}
            </span>
          </>
        )}
      </div>
    </div>
  );
}

export function NotificationPreferencesPage() {
  const query = useNotificationPreferences();
  return (
    <QueryState query={query} errorTitle="Your preferences couldn’t be loaded">
      {(prefs) => <PreferencesMatrix prefs={prefs} />}
    </QueryState>
  );
}
