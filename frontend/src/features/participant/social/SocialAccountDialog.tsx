import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { useToast } from '@/components/ui/toastContext';
import { countryOptions, languageOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { pluralize } from '@/lib/format/text';
import { qk } from '../api/queries';
import type { SocialAccount, SocialAccountChangeResponse, SocialPlatform } from '../api/types';
import { firstMessage, focusFirstError, mapFormErrors } from '../lib/formErrors';
import { platformOptions } from '../lib/labels';

export const SOCIAL_FIELDS = [
  'platform',
  'handle',
  'profileUrl',
  'accountCreatedAt',
  'followerCount',
  'primaryLanguage',
  'audienceCountryCode',
] as const;
type Field = (typeof SOCIAL_FIELDS)[number];

const CODE_TO_FIELD: Record<string, Field> = {
  'social.already_registered': 'handle',
  'social.already_added': 'handle',
  'social.invalid_platform': 'platform',
};

export interface SocialAccountDialogProps {
  open: boolean;
  onClose: () => void;
  /** Present when editing. */
  account?: SocialAccount | null;
  minAccountAgeDays: number;
  /** Called with the server message after an edit (e.g. the verification-reset notice). */
  onSaved?: (message: string | null, verificationReset: boolean) => void;
}

const today = () => new Date().toISOString().slice(0, 10);

/** Add or edit a social profile. All qualification rules are evaluated by the API. */
export function SocialAccountDialog({
  open,
  onClose,
  account,
  minAccountAgeDays,
  onSaved,
}: SocialAccountDialogProps) {
  const editing = !!account;
  const toast = useToast();
  const client = useQueryClient();
  const firstRef = useRef<HTMLSelectElement & HTMLInputElement>(null);

  const [platform, setPlatform] = useState<string>(account?.platform ?? '');
  const [handle, setHandle] = useState(account?.handle ?? '');
  const [profileUrl, setProfileUrl] = useState(account?.profileUrl ?? '');
  const [createdAt, setCreatedAt] = useState(account ? account.accountCreatedAt.slice(0, 10) : '');
  const [followers, setFollowers] = useState(account ? String(account.followerCount) : '');
  const [language, setLanguage] = useState(account?.primaryLanguage ?? '');
  const [country, setCountry] = useState(account?.audienceCountryCode ?? '');
  const [clientErrors, setClientErrors] = useState<Partial<Record<Field, string>>>({});

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        handle: handle.trim().replace(/^@/, ''),
        profileUrl: profileUrl.trim(),
        // A calendar date: sent as UTC midnight so it never shifts across zones.
        accountCreatedAt: `${createdAt}T00:00:00Z`,
        followerCount: Number(followers || 0),
        primaryLanguage: language || null,
        audienceCountryCode: country || null,
      };
      if (account) {
        return api.put<SocialAccountChangeResponse>(`/me/social-accounts/${account.id}`, {
          ...body,
          concurrencyStamp: account.concurrencyStamp,
        });
      }
      const created = await api.post<SocialAccount>('/me/social-accounts', { ...body, platform });
      return {
        account: created,
        verificationReset: false,
        message: '',
      } satisfies SocialAccountChangeResponse;
    },
    onSuccess: async (result) => {
      toast.success(editing ? 'Profile updated' : 'Profile added', result.message || undefined);
      await Promise.all([
        client.invalidateQueries({ queryKey: qk.socialAccounts }),
        client.invalidateQueries({ queryKey: qk.home }),
        client.invalidateQueries({ queryKey: qk.campaigns }),
      ]);
      onSaved?.(result.message || null, result.verificationReset);
      onClose();
    },
    onError: (error) => {
      toast.error(editing ? 'Profile not updated' : 'Profile not added', errorMessage(error));
      const mapped = mapFormErrors(error, SOCIAL_FIELDS, CODE_TO_FIELD);
      if (mapped) focusFirstError('social', SOCIAL_FIELDS, mapped.fields);
    },
  });

  const server = save.isError ? mapFormErrors(save.error, SOCIAL_FIELDS, CODE_TO_FIELD) : null;
  const errorFor = (f: Field) => clientErrors[f] ?? firstMessage(server?.fields[f]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (save.isPending) return;
    const found: Partial<Record<Field, string>> = {};
    if (!editing && !platform) found.platform = 'Choose the platform.';
    if (!handle.trim().replace(/^@/, '')) found.handle = 'Enter your handle.';
    if (!/^https:\/\/\S+$/i.test(profileUrl.trim()))
      found.profileUrl = 'Enter the full https:// link to your profile.';
    if (!createdAt) found.accountCreatedAt = 'Enter the date the account was created.';
    else if (createdAt > today()) found.accountCreatedAt = 'The creation date can’t be in the future.';
    if (followers === '' || !/^\d+$/.test(followers))
      found.followerCount = 'Enter your follower count as a whole number.';
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('social', SOCIAL_FIELDS, found);
      return;
    }
    save.mutate();
  };

  const verifiedish =
    account && (account.verificationStatus === 'Verified' || account.verificationStatus === 'PendingReview');

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={editing ? `Edit @${account.handle}` : 'Add a social profile'}
      description={
        editing
          ? undefined
          : `Profiles must be at least ${pluralize(minAccountAgeDays, 'day')} old (by the platform’s creation date) to qualify.`
      }
      dismissible={!save.isPending}
      initialFocusRef={firstRef}
      footer={
        <>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="submit" form="social-account-form" loading={save.isPending}>
            {editing ? 'Save changes' : 'Add profile'}
          </Button>
        </>
      }
    >
      <form
        id="social-account-form"
        className="pp-form"
        noValidate
        onSubmit={onSubmit}
        aria-label="Social profile"
      >
        {server?.form && (
          <Alert tone="danger" role="alert" title={server.form.title}>
            {server.form.details.length > 0 && (
              <ul>
                {server.form.details.map((d) => (
                  <li key={d}>{d}</li>
                ))}
              </ul>
            )}
          </Alert>
        )}
        {verifiedish && (
          <Alert tone="warning">
            Changing the handle, creation date or follower count sends this profile back for verification.
          </Alert>
        )}
        {editing ? (
          <FormField id="social-platform" label="Platform" hint="The platform can’t be changed.">
            <Input ref={firstRef} value={account.platform} readOnly />
          </FormField>
        ) : (
          <FormField id="social-platform" label="Platform" required error={errorFor('platform')}>
            <Select
              ref={firstRef}
              value={platform}
              placeholder="Choose a platform"
              options={platformOptions}
              onChange={(e) => setPlatform(e.target.value as SocialPlatform)}
            />
          </FormField>
        )}
        <div className="pp-form-grid">
          <FormField id="social-handle" label="Handle" required error={errorFor('handle')}>
            <Input
              value={handle}
              leading={<span aria-hidden="true">@</span>}
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              maxLength={101}
              onChange={(e) => setHandle(e.target.value)}
            />
          </FormField>
          <FormField id="social-followerCount" label="Followers" required error={errorFor('followerCount')}>
            <Input
              type="number"
              inputMode="numeric"
              min={0}
              step={1}
              value={followers}
              onChange={(e) => setFollowers(e.target.value)}
            />
          </FormField>
        </div>
        <FormField
          id="social-profileUrl"
          label="Profile link"
          required
          error={errorFor('profileUrl')}
          hint="For example https://www.instagram.com/yourname"
        >
          <Input
            type="url"
            inputMode="url"
            value={profileUrl}
            onChange={(e) => setProfileUrl(e.target.value)}
          />
        </FormField>
        <FormField
          id="social-accountCreatedAt"
          label="Account created on"
          required
          error={errorFor('accountCreatedAt')}
          hint="Find it in the platform’s “About this account” or account settings. Reviewers check it."
        >
          <Input
            type="date"
            value={createdAt}
            max={today()}
            min="2004-01-01"
            onChange={(e) => setCreatedAt(e.target.value)}
          />
        </FormField>
        <div className="pp-form-grid">
          <FormField
            id="social-primaryLanguage"
            label="Main posting language"
            optional
            error={errorFor('primaryLanguage')}
          >
            <Select
              value={language}
              placeholder="Not specified"
              options={languageOptions()}
              onChange={(e) => setLanguage(e.target.value)}
            />
          </FormField>
          <FormField
            id="social-audienceCountryCode"
            label="Main audience country"
            optional
            error={errorFor('audienceCountryCode')}
          >
            <Select
              value={country}
              placeholder="Not specified"
              options={countryOptions()}
              onChange={(e) => setCountry(e.target.value)}
            />
          </FormField>
        </div>
      </form>
    </Dialog>
  );
}
