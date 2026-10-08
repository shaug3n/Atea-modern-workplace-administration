import React, { useId } from 'react';
import { userFeatureMessages } from './messages';

export type UserWriteReasonError = 'reason_required' | 'reason_too_long' | null;

export function normalizeUserWriteReason(value: string): { reason: string; error: UserWriteReasonError } {
  const reason = value.trim();
  if (!reason) return { reason, error: 'reason_required' };
  if (reason.length > 1000) return { reason, error: 'reason_too_long' };
  return { reason, error: null };
}

export function UserWriteReasonField({ value, onChange, error = null }: { value: string; onChange: (value: string) => void; error?: UserWriteReasonError }) {
  const inputId = useId();
  const hintId = useId();
  const errorId = useId();
  const errorMessage = error === 'reason_too_long'
    ? userFeatureMessages.userFeatureReasonTooLong
    : error === 'reason_required' ? userFeatureMessages.userFeatureReasonRequired : null;

  return (
    <div className="mutation-phrase">
      <label htmlFor={inputId}>{userFeatureMessages.userFeatureReasonLabel}</label>
      <textarea
        id={inputId}
        value={value}
        required
        aria-invalid={Boolean(errorMessage)}
        aria-describedby={`${hintId}${errorMessage ? ` ${errorId}` : ''}`}
        onChange={(event) => onChange(event.target.value)}
      />
      <span id={hintId} className="mutation-reason-hint">{userFeatureMessages.userFeatureReasonHint}</span>
      {errorMessage && <span id={errorId} className="mutation-reason-required">{errorMessage}</span>}
    </div>
  );
}

export function getUserWriteReasonErrorMessage(error: UserWriteReasonError) {
  return error === 'reason_too_long'
    ? userFeatureMessages.userFeatureReasonTooLong
    : error === 'reason_required' ? userFeatureMessages.userFeatureReasonRequired : null;
}

export function getUserWriteReasonHint(validationError: string | null) {
  return validationError
    ? `${userFeatureMessages.userFeatureReasonHint} ${validationError}`
    : userFeatureMessages.userFeatureReasonHint;
}
