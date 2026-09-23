import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ExternalLink, KeyRound } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Button, Checkbox, Dialog, FormField, Input, PasswordInput, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isSafeHref } from '@/lib/safeHref';
import { fieldErrors, firstError } from '../seo/common';
import { integrationKeys, type Connection, type Provider, type ProviderField, type SecretState } from './api';

/**
 * A write-only secret input. A saved secret is never shown or pre-filled: the field shows "Saved" and stays empty
 * until the user chooses to replace it. Leaving it empty keeps the saved value.
 */
export function SecretField({
  field,
  state,
  value,
  onChange,
  cleared,
  onClearedChange,
  error,
}: {
  field: ProviderField;
  state?: SecretState;
  value: string;
  onChange: (value: string) => void;
  cleared: boolean;
  onClearedChange: (cleared: boolean) => void;
  error?: string;
}) {
  const saved = !!state?.saved;
  const [replacing, setReplacing] = useState(!saved);
  if (saved && !replacing) {
    return (
      <div className="int-secret" role="group" aria-label={field.label}>
        <span className="ui-field__label">{field.label}</span>
        <p className="int-secret__saved">
          <KeyRound aria-hidden="true" />
          <span>{cleared ? 'Will be removed when you save' : 'Saved ••••••••'}</span>
        </p>
        <div className="cluster">
          <Button size="sm" variant="secondary" onClick={() => setReplacing(true)} disabled={cleared}>
            Replace {field.label.toLowerCase()}
          </Button>
          {!field.required && <Checkbox label="Remove this secret" checked={cleared} onChange={(e) => onClearedChange(e.target.checked)} />}
        </div>
      </div>
    );
  }
  return (
    <FormField
      label={field.label}
      required={field.required && !saved}
      optional={!field.required || saved}
      hint={saved ? 'Leave empty to keep the saved value.' : (field.help ?? 'Stored encrypted. It can’t be viewed again after saving.')}
      error={error}
    >
      <PasswordInput
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoComplete="new-password"
        spellCheck={false}
        placeholder={saved ? 'Enter a new value' : (field.placeholder ?? undefined)}
        maxLength={field.maxLength}
      />
    </FormField>
  );
}

/** Connect a provider (create) or edit an existing connection's settings and replace secrets. */
export function ConnectionDialog({
  provider,
  connection,
  clientAccountId,
  onClose,
}: {
  provider: Provider;
  connection?: Connection;
  clientAccountId: string | null;
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(connection?.displayName ?? provider.name);
  const [settings, setSettings] = useState<Record<string, string>>(() =>
    Object.fromEntries(provider.settings.map((f) => [f.key, connection?.settings[f.key] ?? ''])),
  );
  const [secrets, setSecrets] = useState<Record<string, string>>({});
  const [cleared, setCleared] = useState<string[]>([]);
  const [expiresAt, setExpiresAt] = useState(connection?.expiresAt?.slice(0, 10) ?? '');

  const save = useMutation({
    mutationFn: () => {
      const cleanSecrets = Object.fromEntries(Object.entries(secrets).filter(([, v]) => v.trim() !== ''));
      const cleanSettings = Object.fromEntries(Object.entries(settings).filter(([, v]) => v.trim() !== ''));
      const expires = expiresAt ? `${expiresAt}T00:00:00Z` : null;
      return connection
        ? api.put<Connection>(`/agency/integrations/connections/${connection.id}`, {
            displayName,
            settings: cleanSettings,
            secrets: cleanSecrets,
            clearSecrets: cleared,
            expiresAt: expires,
            concurrencyStamp: connection.concurrencyStamp,
          })
        : api.post<Connection>('/agency/integrations/connections', {
            provider: provider.key,
            clientAccountId,
            displayName,
            settings: cleanSettings,
            secrets: cleanSecrets,
            expiresAt: expires,
          });
    },
    onSuccess: (saved) => {
      // Never keep typed secrets around longer than needed.
      setSecrets({});
      toast.success(connection ? 'Connection updated' : `${provider.name} connected`, saved.statusMessage ?? undefined);
      void queryClient.invalidateQueries({ queryKey: integrationKeys.all });
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  const err = (key: string) => firstError(errors, key);
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={connection ? `Edit ${provider.name}` : `Connect ${provider.name}`}
      description={provider.helpText}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="int-connection-form" loading={save.isPending}>
            {connection ? 'Save changes' : 'Save connection'}
          </Button>
        </>
      }
    >
      <form id="int-connection-form" className="stack" onSubmit={submit} noValidate autoComplete="off">
        {save.isError && Object.keys(errors).length === 0 && (
          <p role="alert" className="int-error">
            {(save.error as Error).message}
          </p>
        )}
        {provider.docsUrl && isSafeHref(provider.docsUrl) && (
          <a className="ui-link int-docs" href={provider.docsUrl} target="_blank" rel="noopener noreferrer">
            Provider documentation <ExternalLink aria-hidden="true" />
          </a>
        )}
        <FormField label="Connection name" required error={err('displayName')}>
          <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </FormField>
        {provider.settings.map((f) => (
          <FormField key={f.key} label={f.label} required={f.required} optional={!f.required} hint={f.help ?? undefined} error={err(`settings.${f.key}`) ?? err(f.key)}>
            <Input
              value={settings[f.key] ?? ''}
              placeholder={f.placeholder ?? undefined}
              maxLength={f.maxLength}
              spellCheck={false}
              onChange={(e) => setSettings((s) => ({ ...s, [f.key]: e.target.value }))}
            />
          </FormField>
        ))}
        {provider.secrets.length > 0 && (
          <fieldset className="stack int-secrets">
            <legend className="ui-field__label">Credentials</legend>
            <p className="text-small text-muted">Secrets are encrypted at rest, never shown again and never written to the audit log.</p>
            {provider.secrets.map((f) => (
              <SecretField
                key={f.key}
                field={f}
                state={connection?.secrets.find((s) => s.key === f.key)}
                value={secrets[f.key] ?? ''}
                onChange={(v) => setSecrets((s) => ({ ...s, [f.key]: v }))}
                cleared={cleared.includes(f.key)}
                onClearedChange={(c) => setCleared((list) => (c ? [...list, f.key] : list.filter((k) => k !== f.key)))}
                error={err(`secrets.${f.key}`) ?? err(f.key)}
              />
            ))}
          </fieldset>
        )}
        {provider.tokensExpire && (
          <FormField label="Token expires on" optional hint="We remind the team 14 days before a token expires." error={err('expiresAt')}>
            <Input type="date" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
          </FormField>
        )}
      </form>
    </Dialog>
  );
}
