import { errorMessage, isApiError } from '@/lib/api/errors';

/**
 * Human messages for every error code in docs/api/ledger-payouts.md (plus the shared ones). The backend title is used
 * for codes not listed here, so new codes still show something meaningful.
 */
export const FINANCE_ERROR_MESSAGES: Record<string, string> = {
  // Shared
  'auth.forbidden': 'You don’t have permission to do this. Ask an administrator if you need access.',
  http_403: 'You don’t have permission to do this. Ask an administrator if you need access.',
  'concurrency.conflict':
    'Someone else changed this while you were looking at it. Refresh to see the latest version, then try again.',
  'request.confirm_required': 'This action needs an explicit confirmation. Please confirm and try again.',
  'user.not_found': 'We couldn’t find that user. Check the user id.',
  'submission.not_found': 'We couldn’t find that submission.',
  'supportticket.not_found': 'We couldn’t find that support ticket.',
  'payoutbatch.not_found': 'This payout batch no longer exists.',
  'payoutitem.not_found': 'This payout item no longer exists.',
  'earning.not_found': 'This earning no longer exists.',
  'payout.not_found': 'This payout no longer exists.',
  'payouthold.not_found': 'This hold no longer exists.',

  // Ledger
  'ledger.zero_amount': 'The adjustment amount can’t be zero.',
  'ledger.currency_unsupported': 'That currency isn’t supported for payouts.',
  'ledger.submission_mismatch': 'That submission doesn’t belong to this participant.',
  'ledger.ticket_mismatch': 'That support ticket doesn’t belong to this participant.',
  'ledger.request_id_reused':
    'This request was already used for a different adjustment. Close the dialog and start a new adjustment.',
  'fx.rate_missing':
    'There is no exchange rate from this currency to the settlement currency. Add a rate first, then try again.',
  'ledger.in_payout_batch':
    'This earning is part of a payout batch. Hold or remove it from the batch (or wait until it is paid) before reversing it.',
  'ledger.already_reversed': 'This earning has already been reversed.',
  'ledger.cannot_reverse_reversal': 'A reversal can’t itself be reversed.',
  'ledger.cannot_reverse_debit': 'Debits can’t be reversed. Create a positive adjustment instead.',
  'ledger.declined': 'Declined earnings were never payable, so there is nothing to reverse.',
  'ledger.self_approval':
    'You created this earning (or it is credited to you), so a different finance user must approve or decline it.',
  'ledger.self_adjustment': 'You can’t create an adjustment on your own account. Ask another finance user.',
  'ledger.awaiting_live_check':
    'This reward is waiting for the post’s live check. It can be approved once the check has passed.',
  'ledger.not_pending': 'This earning is no longer pending — someone else may have decided it already.',
  'ledger.reason_required': 'Please give a reason.',
  'ledger.negative_amount': 'The amount must be positive.',

  // Exchange rates
  'fx.same_currency': 'Base and quote currency must be different.',
  'fx.invalid_rate': 'The rate must be greater than 0 and at most 1,000,000 (up to 8 decimals).',
  'fx.effective_too_early': 'The effective time can be at most one day in the past.',
  'fx.duplicate': 'A rate for this currency pair and effective time already exists.',

  // Schedule
  'payout.invalid_time_zone': 'That time zone isn’t recognised. Use an IANA time zone such as Europe/London.',
  'payout.effective_in_past': 'A schedule change can’t take effect in the past.',
  'payout.invalid_anchor': 'The anchor cutoff date must be between 2000 and 2100.',
  'payout.settlement_currency_in_use':
    'Unpaid earnings are still settled in the current currency. Pay out, reverse or decline them before changing the settlement currency.',

  // Holds
  'payout.hold_exists': 'This participant already has an active payout hold.',
  'payout.hold_not_active': 'This hold has already been released.',

  // Batches
  'payout.invalid_period': 'That date is not a cutoff date of the current payout schedule.',
  'payout.period_not_completed':
    'This period hasn’t reached its cutoff yet. Batches can only be prepared for completed periods.',
  'payout.no_eligible_earnings':
    'There is nothing to pay for this period: no approved earnings are available at its cutoff.',
  'payout.earnings_changed':
    'Some earnings changed while the batch was being updated. Nothing was saved — please try again.',
  'payout.prepare_busy': 'Another preparation is running right now. Wait a moment and try again.',
  'payout.not_draft':
    'This batch is no longer a draft — it may have been finalized or cancelled by someone else.',
  'payout.item_state': 'This item can’t be changed in its current state.',
  'payout.item_not_held': 'This item is not on hold.',
  'payout.no_payout_profile':
    'This participant has no payout details on file, so the item must stay on hold.',
  'payout.user_on_hold': 'This participant has an active payout hold. Release the hold first.',
  'payout.user_inactive': 'This participant’s account is suspended or deactivated.',
  'payout.settlement_currency_changed':
    'The settlement currency changed since this batch was prepared. Cancel it and prepare a new batch.',
  'payout.cannot_cancel': 'Completed or cancelled batches can’t be cancelled.',
  'payout.has_paid_items': 'This batch already has paid items, so it can’t be cancelled.',
  'payout.self_finalize': 'A different finance user must finalize a batch you prepared.',
  'payout.conflict_of_interest':
    'You are paid in this batch, or you created or approved one of its earnings, so a different finance user must finalize it.',
  'payout.self_record':
    'The system prepared this batch and you finalized it, so a different finance user must record its payments.',
  'payout.batch_not_finalized': 'This is only possible for finalized batches.',
  'payout.paid_at_in_future': 'The payment date can’t be in the future.',
  'payout.invalid_reference': 'Enter a valid payment reference (3–120 characters).',
  'payout.already_recorded': 'A payment was already recorded for this item by someone else.',
  'payout.item_not_awaiting_payment': 'This item is not awaiting payment any more.',
  'payout.bulk_size': 'Send between 1 and 1,000 payment lines.',
};

/** Human message for an API error (known code → curated text, otherwise the server title). */
export function financeErrorMessage(error: unknown): string {
  if (isApiError(error)) {
    const known = FINANCE_ERROR_MESSAGES[error.code];
    if (known) return known;
    // A missing permission gets the generic text; any other 403 is a specific refusal (segregation of duties, four-eyes,
    // conflict of interest…) whose reason the server states — never tell that user to ask for more access.
    if (error.status === 403 && (error.code === 'auth.forbidden' || error.code.startsWith('http_')))
      return FINANCE_ERROR_MESSAGES['auth.forbidden']!;
    const fields = error.errors ? Object.values(error.errors).flat() : [];
    if (error.status === 400 && fields.length > 0) return fields.join(' ');
    return error.title;
  }
  return errorMessage(error);
}

/** Error code of an API error ('' otherwise). */
export function errorCode(error: unknown): string {
  return isApiError(error) ? error.code : '';
}

/** Rethrows with a human message (ConfirmDialog shows `Error.message`). */
export function humanError(error: unknown): Error {
  return new Error(financeErrorMessage(error));
}
