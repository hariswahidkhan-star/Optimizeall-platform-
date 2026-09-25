import { useMutation, useQueryClient } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { useState, type FormEvent, type KeyboardEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { Checkbox } from '@/components/ui/Checkbox';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input } from '@/components/ui/Input';
import { Select } from '@/components/ui/Select';
import { Switch } from '@/components/ui/Switch';
import { useToast } from '@/components/ui/toastContext';
import { countryOptions, languageOptions, timeZoneOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { qk, useProfile } from '../api/queries';
import type { Profile, UpdateProfileRequest } from '../api/types';
import { QueryState } from '../components/QueryState';
import { firstMessage, focusFirstError, mapFormErrors } from '../lib/formErrors';
import '../participant.css';
import { EarnedBadges } from '../learning/EarnedBadges';

const FIELDS = [
  'displayName',
  'countryCode',
  'languageCode',
  'timeZone',
  'interests',
  'whatsAppNumber',
  'whatsAppOptIn',
] as const;
type Field = (typeof FIELDS)[number];

const MAX_INTERESTS = 20;
const E164 = /^\+[1-9]\d{7,14}$/;

function ProfileForm({ profile }: { profile: Profile }) {
  const toast = useToast();
  const client = useQueryClient();
  const { refreshUser } = useAuth();
  const [displayName, setDisplayName] = useState(profile.displayName);
  const [countryCode, setCountryCode] = useState(profile.countryCode);
  const [languageCode, setLanguageCode] = useState(profile.languageCode);
  const [timeZone, setTimeZone] = useState(profile.timeZone);
  const [interests, setInterests] = useState<string[]>(profile.interests);
  const [interestDraft, setInterestDraft] = useState('');
  const [marketing, setMarketing] = useState(profile.marketingEmailOptIn);
  const [whatsAppNumber, setWhatsAppNumber] = useState(profile.whatsAppNumber ?? '');
  const [whatsAppOptIn, setWhatsAppOptIn] = useState(profile.whatsAppOptIn);
  const [clientErrors, setClientErrors] = useState<Partial<Record<Field, string>>>({});

  const save = useMutation({
    mutationFn: (request: UpdateProfileRequest) => api.put<Profile>('/me/profile', request),
    onSuccess: async (updated) => {
      client.setQueryData(qk.profile, updated);
      setWhatsAppNumber(updated.whatsAppNumber ?? '');
      setInterests(updated.interests);
      toast.success('Profile saved');
      await Promise.all([
        client.invalidateQueries({ queryKey: qk.home }),
        client.invalidateQueries({ queryKey: qk.notificationPreferences }),
        client.invalidateQueries({ queryKey: qk.campaigns }),
      ]);
      // Time zone and display name live in the session too.
      await refreshUser().catch(() => null);
    },
    onError: (error) => {
      toast.error('Profile not saved', errorMessage(error));
      const mapped = mapFormErrors(error, FIELDS);
      if (mapped) focusFirstError('profile', FIELDS, mapped.fields);
    },
  });
  const server = save.isError ? mapFormErrors(save.error, FIELDS) : null;
  const errorFor = (f: Field) => clientErrors[f] ?? firstMessage(server?.fields[f]);

  const addInterest = () => {
    const tag = interestDraft.trim().toLowerCase();
    if (!tag) return;
    if (tag.length > 40) {
      setClientErrors((e) => ({ ...e, interests: 'Each interest can be up to 40 characters.' }));
      return;
    }
    if (interests.length >= MAX_INTERESTS) {
      setClientErrors((e) => ({ ...e, interests: `You can add up to ${MAX_INTERESTS} interests.` }));
      return;
    }
    if (!interests.includes(tag)) setInterests([...interests, tag]);
    setInterestDraft('');
    setClientErrors((e) => ({ ...e, interests: undefined }));
  };

  const onInterestKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      addInterest();
    }
  };

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (save.isPending) return;
    const found: Partial<Record<Field, string>> = {};
    const name = displayName.trim();
    if (name.length < 2 || name.length > 100) found.displayName = 'Use 2 to 100 characters.';
    if (!countryCode) found.countryCode = 'Choose your country.';
    if (!timeZone) found.timeZone = 'Choose your time zone.';
    const number = whatsAppNumber.replace(/[\s-]/g, '');
    if (number && !E164.test(number)) found.whatsAppNumber = 'Use international format, e.g. +923001234567.';
    if (whatsAppOptIn && !number)
      found.whatsAppNumber = 'Add your WhatsApp number to turn on WhatsApp messages.';
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('profile', FIELDS, found);
      return;
    }
    save.mutate({
      displayName: name,
      countryCode,
      languageCode,
      timeZone,
      interests,
      marketingEmailOptIn: marketing,
      whatsAppNumber: number || null,
      whatsAppOptIn,
    });
  };

  return (
    <form className="pp-form" noValidate onSubmit={onSubmit} aria-label="Profile details">
      {server?.form && (
        <Alert tone="danger" role="alert" title={server.form.title}>
          {server.form.details.join(' ')}
        </Alert>
      )}
      <Card as="section" aria-labelledby="profile-about-title">
        <CardHeader
          titleId="profile-about-title"
          title="About you"
          description={`Signed in as ${profile.email}`}
        />
        <CardBody className="pp-form">
          <FormField id="profile-displayName" label="Display name" required error={errorFor('displayName')}>
            <Input
              value={displayName}
              maxLength={100}
              autoComplete="name"
              onChange={(e) => setDisplayName(e.target.value)}
            />
          </FormField>
          <div className="pp-form-grid">
            <FormField id="profile-countryCode" label="Country" required error={errorFor('countryCode')}>
              <Select
                value={countryCode}
                options={countryOptions()}
                onChange={(e) => setCountryCode(e.target.value)}
              />
            </FormField>
            <FormField id="profile-languageCode" label="Language" required error={errorFor('languageCode')}>
              <Select
                value={languageCode}
                options={languageOptions()}
                onChange={(e) => setLanguageCode(e.target.value)}
              />
            </FormField>
          </div>
          <FormField
            id="profile-timeZone"
            label="Time zone"
            required
            error={errorFor('timeZone')}
            hint="Dates, deadlines and your post times are shown in this zone."
          >
            <Select
              value={timeZone}
              options={timeZoneOptions(timeZone)}
              onChange={(e) => setTimeZone(e.target.value)}
            />
          </FormField>
          <FormField
            id="profile-interests"
            label="Interests"
            error={errorFor('interests')}
            hint={`Press Enter to add. Up to ${MAX_INTERESTS}. We use them to recommend campaigns.`}
          >
            <Input
              value={interestDraft}
              maxLength={40}
              placeholder="e.g. fitness"
              onChange={(e) => setInterestDraft(e.target.value)}
              onKeyDown={onInterestKey}
              onBlur={addInterest}
            />
          </FormField>
          {interests.length > 0 && (
            <ul className="pp-chip-list" aria-label="Your interests">
              {interests.map((tag) => (
                <li key={tag} className="pp-chip">
                  {tag}
                  <IconButton
                    size="sm"
                    label={`Remove interest ${tag}`}
                    icon={<X />}
                    onClick={() => setInterests(interests.filter((t) => t !== tag))}
                  />
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      <Card as="section" aria-labelledby="profile-contact-title">
        <CardHeader titleId="profile-contact-title" title="Messages from us" />
        <CardBody className="pp-form">
          <Checkbox
            label="Email me about new campaigns and tips"
            description="Optional. Account, submission and payout emails are always sent."
            checked={marketing}
            onChange={(e) => setMarketing(e.target.checked)}
          />
          <FormField
            id="profile-whatsAppNumber"
            label="WhatsApp number"
            optional
            error={errorFor('whatsAppNumber')}
            hint="International format with country code, e.g. +923001234567. Turning WhatsApp off removes the number."
          >
            <Input
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              value={whatsAppNumber}
              onChange={(e) => setWhatsAppNumber(e.target.value)}
            />
          </FormField>
          <Switch
            checked={whatsAppOptIn}
            onCheckedChange={setWhatsAppOptIn}
            label="Send me notifications on WhatsApp"
            description="Choose which ones under Notifications."
          />
        </CardBody>
      </Card>
      <div>
        <Button type="submit" loading={save.isPending}>
          Save profile
        </Button>
      </div>
    </form>
  );
}

export function ProfileDetailsPage() {
  const query = useProfile();
  return (
    <QueryState query={query} errorTitle="Your profile couldn’t be loaded">
      {(profile) => (
        <div className="stack">
          <ProfileForm profile={profile} />
          <EarnedBadges />
        </div>
      )}
    </QueryState>
  );
}
