import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Lock, ShieldCheck } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { FormField } from '@/components/ui/FormField';
import { Input } from '@/components/ui/Input';
import { KeyValueList } from '@/components/ui/KeyValueList';
import { RadioGroup } from '@/components/ui/RadioGroup';
import { Select } from '@/components/ui/Select';
import { useToast } from '@/components/ui/toastContext';
import { countryName, countryOptions } from '@/features/auth/localeOptions';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { qk, usePayoutProfile } from '../api/queries';
import type { PayoutMethod, PayoutProfile, UpdatePayoutProfileRequest } from '../api/types';
import { QueryState } from '../components/QueryState';
import { firstMessage, focusFirstError, mapFormErrors } from '../lib/formErrors';
import { payoutMethodOptions, SUPPORTED_CURRENCIES } from '../lib/labels';
import '../participant.css';

const FIELDS = ['method', 'accountHolderName', 'destination', 'preferredCurrency', 'countryCode'] as const;
type Field = (typeof FIELDS)[number];

const DESTINATION: Record<
  PayoutMethod,
  { label: string; hint: string; type: string; inputMode?: 'email' | 'tel' | 'text' }
> = {
  BankTransfer: {
    label: 'IBAN or account number',
    hint: '8–34 letters and digits. Spaces and dashes are removed. IBANs are checked for typos.',
    type: 'text',
    inputMode: 'text',
  },
  PayPal: {
    label: 'PayPal email address',
    hint: 'The email address of your PayPal account.',
    type: 'email',
    inputMode: 'email',
  },
  MobileWallet: {
    label: 'Wallet phone number',
    hint: 'International format with country code, e.g. +923001234567.',
    type: 'tel',
    inputMode: 'tel',
  },
  Other: {
    label: 'Payment details',
    hint: 'How finance should pay you (3–200 characters).',
    type: 'text',
    inputMode: 'text',
  },
};

function validateDestination(method: PayoutMethod, raw: string): string | null {
  const value = raw.trim();
  if (!value) return 'Enter where we should send your payouts.';
  switch (method) {
    case 'PayPal':
      return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value) ? null : 'Enter a valid email address.';
    case 'MobileWallet':
      return /^\+[1-9]\d{7,14}$/.test(value.replace(/[\s-]/g, ''))
        ? null
        : 'Use international format, e.g. +923001234567.';
    case 'BankTransfer':
      return /^[A-Za-z0-9]{8,34}$/.test(value.replace(/[\s-]/g, '')) ? null : 'Use 8–34 letters and digits.';
    default:
      return value.length >= 3 ? null : 'Enter at least 3 characters.';
  }
}

function PayoutForm({ current }: { current: PayoutProfile }) {
  const toast = useToast();
  const client = useQueryClient();
  const [method, setMethod] = useState<PayoutMethod | ''>(current.method ?? '');
  const [holder, setHolder] = useState(current.accountHolderName ?? '');
  const [destination, setDestination] = useState('');
  const [currency, setCurrency] = useState(current.preferredCurrency ?? 'USD');
  const [country, setCountry] = useState(current.countryCode ?? '');
  const [clientErrors, setClientErrors] = useState<Partial<Record<Field, string>>>({});

  const save = useMutation({
    mutationFn: (body: UpdatePayoutProfileRequest) => api.put<PayoutProfile>('/me/payout-profile', body),
    onSuccess: (updated) => {
      client.setQueryData(qk.payoutProfile, updated);
      void client.invalidateQueries({ queryKey: qk.home });
      setDestination('');
      toast.success(
        'Payout details saved',
        `We’ll pay to ${updated.destinationHint ?? 'your new destination'}.`,
      );
    },
    onError: (error) => {
      toast.error('Payout details not saved', errorMessage(error));
      const mapped = mapFormErrors(error, FIELDS);
      if (mapped) focusFirstError('payout', FIELDS, mapped.fields);
    },
  });
  const server = save.isError ? mapFormErrors(save.error, FIELDS) : null;
  const errorFor = (f: Field) => clientErrors[f] ?? firstMessage(server?.fields[f]);

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (save.isPending) return;
    const found: Partial<Record<Field, string>> = {};
    if (!method) found.method = 'Choose how you want to be paid.';
    if (holder.trim().length < 2) found.accountHolderName = 'Enter the account holder’s full name.';
    if (method) {
      const problem = validateDestination(method, destination);
      if (problem) found.destination = problem;
    }
    if (!currency) found.preferredCurrency = 'Choose a currency.';
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstError('payout', FIELDS, found);
      return;
    }
    save.mutate({
      method: method as PayoutMethod,
      accountHolderName: holder.trim(),
      destination: destination.trim(),
      preferredCurrency: currency,
      countryCode: country || null,
    });
  };

  const meta = method ? DESTINATION[method] : null;

  return (
    <form className="pp-form" noValidate onSubmit={onSubmit} aria-label="Payout details">
      {server?.form && (
        <Alert tone="danger" role="alert">
          {server.form.title}
        </Alert>
      )}
      <div id="payout-method" tabIndex={-1}>
        <RadioGroup
          legend="Payout method"
          value={method || null}
          onChange={(v) => {
            setMethod(v as PayoutMethod);
            setDestination('');
          }}
          options={payoutMethodOptions}
          orientation="horizontal"
          variant="cards"
          error={errorFor('method')}
          required
        />
      </div>
      <FormField
        id="payout-accountHolderName"
        label="Account holder name"
        required
        error={errorFor('accountHolderName')}
      >
        <Input
          value={holder}
          maxLength={150}
          autoComplete="name"
          onChange={(e) => setHolder(e.target.value)}
        />
      </FormField>
      {meta && (
        <FormField
          id="payout-destination"
          label={meta.label}
          required
          error={errorFor('destination')}
          hint={
            current.configured
              ? `${meta.hint} For your security the saved value is never shown — enter it again to save changes.`
              : meta.hint
          }
        >
          <Input
            type={meta.type}
            inputMode={meta.inputMode}
            autoComplete="off"
            spellCheck={false}
            maxLength={200}
            value={destination}
            onChange={(e) => setDestination(e.target.value)}
          />
        </FormField>
      )}
      <div className="pp-form-grid">
        <FormField
          id="payout-preferredCurrency"
          label="Preferred currency"
          required
          error={errorFor('preferredCurrency')}
        >
          <Select
            value={currency}
            options={SUPPORTED_CURRENCIES.map((c) => ({ value: c, label: c }))}
            onChange={(e) => setCurrency(e.target.value)}
          />
        </FormField>
        <FormField
          id="payout-countryCode"
          label="Country of the account"
          optional
          error={errorFor('countryCode')}
        >
          <Select
            value={country}
            placeholder="Not specified"
            options={countryOptions()}
            onChange={(e) => setCountry(e.target.value)}
          />
        </FormField>
      </div>
      <div>
        <Button type="submit" loading={save.isPending} leadingIcon={<Lock />}>
          Save payout details
        </Button>
      </div>
    </form>
  );
}

export function PayoutDetailsPage() {
  const query = usePayoutProfile();
  return (
    <QueryState query={query} errorTitle="Your payout details couldn’t be loaded">
      {(current) => (
        <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-6)' }}>
          <Alert tone="info" icon={<ShieldCheck />} title="What we collect and why">
            <p>
              We only ask for what finance needs to pay you: the payout method, the account holder’s name,
              where to send the money, your preferred currency and the account’s country. The account number,
              email or phone number is encrypted when saved, is never shown again in full (not even to you),
              and is only used by our finance team when paying you.
            </p>
          </Alert>
          <Card as="section" aria-labelledby="payout-current-title">
            <CardHeader titleId="payout-current-title" title="Current payout destination" />
            <CardBody>
              {current.configured ? (
                <KeyValueList
                  items={[
                    {
                      label: 'Method',
                      value:
                        payoutMethodOptions.find((o) => o.value === current.method)?.label ?? current.method,
                    },
                    { label: 'Account holder', value: current.accountHolderName },
                    { label: 'Destination', value: <code>{current.destinationHint}</code> },
                    { label: 'Currency', value: current.preferredCurrency },
                    { label: 'Country', value: current.countryCode ? countryName(current.countryCode) : '—' },
                    { label: 'Last updated', value: <DateTime value={current.updatedAt} /> },
                  ]}
                />
              ) : (
                <Alert tone="warning" title="No payout details yet">
                  Add them now so your approved earnings can be paid in the next payout.
                </Alert>
              )}
            </CardBody>
          </Card>
          <Card as="section" aria-labelledby="payout-form-title">
            <CardHeader
              titleId="payout-form-title"
              title={current.configured ? 'Change payout details' : 'Add payout details'}
            />
            <CardBody>
              <PayoutForm current={current} />
            </CardBody>
          </Card>
        </div>
      )}
    </QueryState>
  );
}
