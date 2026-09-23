import { useRef, useState, type FormEvent } from 'react';
import { Alert, Button, Checkbox, FormField, Input, Textarea } from '@/components/ui';
import { billingErrorMessage, formatDateOnly } from '@/features/agency/billing/lib';
import { formatDate } from '@/lib/format/dates';
import type { AcceptProposalResponse, PublicProposal } from '../api/types';
import '../crm.css';

export interface AcceptBody {
  version: number;
  fullName: string;
  title: string;
  email?: string;
  agreeToTerms: boolean;
}

export interface AcceptProposalPanelProps {
  proposal: PublicProposal;
  askEmail?: boolean;
  onAccept: (body: AcceptBody) => Promise<AcceptProposalResponse>;
  onDecline: (body: { version: number; reason: string }) => Promise<unknown>;
}

/** Typed-signature acceptance ("I agree to the terms") or decline with a reason. The server records time, IP hash and browser. */
export function AcceptProposalPanel({ proposal, askEmail, onAccept, onDecline }: AcceptProposalPanelProps) {
  const [fullName, setFullName] = useState('');
  const [title, setTitle] = useState('');
  const [email, setEmail] = useState('');
  const [agree, setAgree] = useState(false);
  const [touched, setTouched] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<AcceptProposalResponse | null>(null);
  const [declining, setDeclining] = useState(false);
  const [reason, setReason] = useState('');
  const inFlight = useRef(false);

  if (proposal.status === 'Accepted' || done) {
    const signer = done?.proposal.signerName ?? proposal.signerName;
    const at = done?.proposal.acceptedAt ?? proposal.acceptedAt;
    return (
      <Alert tone="success" title="Proposal accepted">
        Accepted by {signer}
        {at ? ` on ${formatDate(at)}` : ''}. Thank you — we’ll be in touch about next steps
        {done?.invitationSent ? ' and we’ve emailed you a link to set up your client portal.' : '.'}
      </Alert>
    );
  }
  if (proposal.status === 'Declined') return <Alert tone="info" title="Proposal declined">Thanks for letting us know.</Alert>;
  if (proposal.beingRevised)
    return <Alert tone="info" title="Being updated">This proposal is being revised. You’ll receive the new version shortly.</Alert>;
  if (proposal.expired)
    return <Alert tone="warning" title="This proposal has expired">It was valid until {formatDateOnly(proposal.version.validUntil)}. Ask us for an updated proposal.</Alert>;
  if (!proposal.canRespond) return <Alert tone="info" title="This proposal is no longer open">Contact us if you have questions.</Alert>;

  const nameError = fullName.trim().length < 2 ? 'Type your full name.' : null;
  const titleError = title.trim().length < 2 ? 'Enter your job title.' : null;
  const agreeError = !agree ? 'Please confirm that you agree to the terms.' : null;

  const run = async (event: FormEvent, action: () => Promise<void>) => {
    event.preventDefault();
    if (inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err);
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  };

  if (declining)
    return (
      <form
        className="crm-signature"
        aria-label="Decline proposal"
        noValidate
        onSubmit={(e) =>
          run(e, async () => {
            if (reason.trim().length < 3) {
              setTouched(true);
              return;
            }
            await onDecline({ version: proposal.version.versionNumber, reason: reason.trim() });
          })
        }
      >
        <FormField label="Why are you declining?" required error={touched && reason.trim().length < 3 ? 'Please tell us why (a few words is fine).' : null}>
          <Textarea rows={3} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
        </FormField>
        {error !== null && (
          <Alert tone="danger" role="alert">
            {billingErrorMessage(error)}
          </Alert>
        )}
        <div className="crm-actions">
          <Button type="submit" variant="danger" loading={busy}>
            Decline proposal
          </Button>
          <Button variant="ghost" onClick={() => setDeclining(false)}>
            Back
          </Button>
        </div>
      </form>
    );

  return (
    <form
      className="crm-signature"
      aria-label="Accept proposal"
      noValidate
      onSubmit={(e) =>
        run(e, async () => {
          setTouched(true);
          if (nameError || titleError || agreeError) return;
          setDone(
            await onAccept({
              version: proposal.version.versionNumber,
              fullName: fullName.trim(),
              title: title.trim(),
              email: askEmail && email.trim() ? email.trim() : undefined,
              agreeToTerms: true,
            }),
          );
        })
      }
    >
      <h2 className="bill-strong">Accept this proposal</h2>
      <p className="crm-muted">Typing your name below is your electronic signature for version {proposal.version.versionNumber}.</p>
      <FormField label="Full name" required error={touched ? nameError : null}>
        <Input autoComplete="name" value={fullName} maxLength={150} onChange={(e) => setFullName(e.target.value)} />
      </FormField>
      <FormField label="Job title" required error={touched ? titleError : null}>
        <Input autoComplete="organization-title" value={title} maxLength={150} onChange={(e) => setTitle(e.target.value)} />
      </FormField>
      {askEmail && (
        <FormField label="Work email" optional hint="We’ll send your client-portal invitation here.">
          <Input type="email" autoComplete="email" value={email} maxLength={254} onChange={(e) => setEmail(e.target.value)} />
        </FormField>
      )}
      <Checkbox
        label="I agree to the terms of this proposal"
        checked={agree}
        invalid={touched && !!agreeError}
        description={touched && agreeError ? agreeError : undefined}
        onChange={(e) => setAgree(e.target.checked)}
      />
      {error !== null && (
        <Alert tone="danger" role="alert">
          {billingErrorMessage(error)}
        </Alert>
      )}
      <div className="crm-actions">
        <Button type="submit" loading={busy}>
          Accept proposal
        </Button>
        <Button variant="ghost" onClick={() => setDeclining(true)}>
          Decline
        </Button>
      </div>
    </form>
  );
}
