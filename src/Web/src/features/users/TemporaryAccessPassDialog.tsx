import React, { useId, useRef, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { ConfirmationDialog } from '../../components/ConfirmationDialog';
import { formatDateTime } from '../../format/dateTime';
import { TechnicalDetails } from '../../components/TechnicalDetails';
import type { ApiFetch } from './userDetailApi';
import { grantTemporaryAccessPass, TemporaryAccessPassRequestError } from './authenticationMethodsApi';
import { useFocusContainment } from '../../components/useFocusContainment';
import { UserWriteReasonField, normalizeUserWriteReason } from './UserWriteReasonField';
import { userFeatureMessages } from './messages';

export function TemporaryAccessPassDialog({ userId, target, onClose, onAuditWarning }: { userId: string; target: string; onClose: () => void; onAuditWarning?: (warning: string | null) => void }) {
  const api = useApi();
  const [pending, setPending] = useState(false);
  const [code, setCode] = useState<string | null>(null);
  const [passDetails, setPassDetails] = useState<{ startDateTime?: string | null; lifetimeInMinutes?: number | null; isUsableOnce?: boolean | null } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [requestIdentifiers, setRequestIdentifiers] = useState<{ graphCorrelationId?: string | null; graphRequestId?: string | null } | null>(null);
  const [auditWarning, setAuditWarning] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<'reason_required' | 'reason_too_long' | null>(null);
  const [lifetimeInMinutes, setLifetimeInMinutes] = useState('60');
  const [isUsableOnce, setIsUsableOnce] = useState(true);
  const [lifetimeError, setLifetimeError] = useState(false);
  const durationId = useId();
  const durationHintId = useId();
  const durationErrorId = useId();
  const close = () => {
    const trigger = Array.from(document.querySelectorAll<HTMLButtonElement>('button')).find((button) => button.textContent?.trim() === 'Grant Temporary Access Pass' && !button.closest('[role="dialog"]'));
    const restoreTarget = trigger ?? returnFocusRef.current;
    returnFocusRef.current = restoreTarget;
    setCode(null);
    setPassDetails(null);
    setError(null);
    setRequestIdentifiers(null);
    onClose();
    restoreTarget?.focus();
  };
  const returnFocusRef = useRef<HTMLElement | null>(document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const resultRef = useFocusContainment<HTMLElement>(Boolean(code), close, returnFocusRef);
  const submit = async () => {
    if (pending) return;
    const normalizedReason = normalizeUserWriteReason(reason);
    if (normalizedReason.error) {
      setReasonError(normalizedReason.error);
      return;
    }
    const lifetime = Number(lifetimeInMinutes);
    if (!Number.isInteger(lifetime) || lifetime < 10 || lifetime > 1440) {
      setLifetimeError(true);
      return;
    }
    setReasonError(null);
    setLifetimeError(false);
    setPending(true); setError(null); setRequestIdentifiers(null);
    try {
      const result = await grantTemporaryAccessPass(api as ApiFetch, userId, normalizedReason.reason, lifetime, isUsableOnce);
      const warning = result.auditWarning?.trim() || null;
      setAuditWarning(warning);
      onAuditWarning?.(warning);
      if (result.status === 'succeeded' && result.temporaryAccessPass && !result.replayed) {
        setPassDetails(result);
        setCode(result.temporaryAccessPass);
      }
      else setError(result.replayed ? userFeatureMessages.userTapReplayNotice : userFeatureMessages.userTapNoSecretReturned);
    } catch (submitError) {
      if (submitError instanceof TemporaryAccessPassRequestError) {
        setRequestIdentifiers(submitError);
        if (submitError.auditWarning) {
          setAuditWarning(submitError.auditWarning);
          onAuditWarning?.(submitError.auditWarning);
        }
        setError(submitError.category === 'tenant_policy_rejected' ? userFeatureMessages.userTapPolicyRejected : userFeatureMessages.userTapCouldNotIssue);
      } else {
        setError(userFeatureMessages.userTapCouldNotIssue);
      }
    }
    finally { setPending(false); }
  };
  const duration = Number(lifetimeInMinutes);
  const adjustDuration = (change: number) => {
    const current = Number.isInteger(duration) ? duration : 60;
    setLifetimeInMinutes(String(Math.max(10, Math.min(1440, current + change))));
    setLifetimeError(false);
    setError(null);
  };
  const expiry = passDetails?.startDateTime && Number.isInteger(passDetails.lifetimeInMinutes)
    ? Date.parse(passDetails.startDateTime) + Number(passDetails.lifetimeInMinutes) * 60_000
    : Number.NaN;
  const proposedLifetime = Number.isInteger(duration) && duration >= 10 && duration <= 1440 ? duration : 60;
  if (code) return <div className="modal-backdrop"><section ref={resultRef} className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="tap-result-title"><h2 id="tap-result-title">{userFeatureMessages.userTapResultTitle}</h2><p>{userFeatureMessages.userTapOneTimeCodeNotice}</p>{passDetails?.lifetimeInMinutes && <p>{userFeatureMessages.userTapUsageNotice(passDetails.lifetimeInMinutes, passDetails.isUsableOnce === true)}</p>}{Number.isFinite(expiry) && <p>{`${userFeatureMessages.userTapExpiresAt}: ${formatDateTime(new Date(expiry).toISOString())}`}</p>}{auditWarning && <p role="alert" className="audit-warning">{auditWarning}</p>}<code className="temporary-access-pass__code">{code}</code><button type="button" className="button button--quiet" onClick={() => void navigator.clipboard?.writeText(code)}>{userFeatureMessages.userTapCopyCode}</button><button type="button" className="button button--secondary" onClick={close}>{userFeatureMessages.userTapClose}</button></section></div>;
  return <ConfirmationDialog title="Grant Temporary Access Pass" target={target} proposedChange={userFeatureMessages.userTapProposedChange(proposedLifetime, isUsableOnce)} requiredCapability="authentication.methods.manage" confirmLabel={messages.confirmIssueTap} busy={pending} confirmBlocked={Boolean(reasonError) || lifetimeError} onConfirm={() => void submit()} onCancel={close}>
    <div className="temporary-access-pass-options">
      <fieldset className="temporary-access-pass-options__presets">
        <legend>{userFeatureMessages.userTapDurationLabel}</legend>
        <div className="temporary-access-pass-options__preset-buttons">
          {[60, 480, 1440].map((minutes, index) => {
            const label = [userFeatureMessages.userTapOneHourPreset, userFeatureMessages.userTapEightHourPreset, userFeatureMessages.userTapTwentyFourHourPreset][index];
            return <button key={minutes} type="button" className="button button--secondary button--sm" aria-pressed={lifetimeInMinutes === String(minutes)} onClick={() => { setLifetimeInMinutes(String(minutes)); setLifetimeError(false); setError(null); }}>{label}</button>;
          })}
        </div>
      </fieldset>
      <div className="temporary-access-pass-options__duration">
        <label htmlFor={durationId}>{userFeatureMessages.userTapDurationMinutesLabel}</label>
        <div className="temporary-access-pass-options__stepper">
          <button type="button" className="button button--secondary" aria-label={userFeatureMessages.userTapDecreaseDuration} disabled={duration <= 10} onClick={() => adjustDuration(-10)}>−10</button>
          <input id={durationId} type="number" min={10} max={1440} step={1} value={lifetimeInMinutes} aria-describedby={lifetimeError ? `${durationHintId} ${durationErrorId}` : durationHintId} aria-invalid={lifetimeError} onChange={(event) => { setLifetimeInMinutes(event.target.value); setLifetimeError(false); setError(null); }} />
          <button type="button" className="button button--secondary" aria-label={userFeatureMessages.userTapIncreaseDuration} disabled={duration >= 1440} onClick={() => adjustDuration(10)}>+10</button>
        </div>
        <span id={durationHintId} className="temporary-access-pass-options__hint">{userFeatureMessages.userTapDurationHint}</span>
        {lifetimeError && <p id={durationErrorId} role="alert" className="mutation-reason-required">{userFeatureMessages.userTapDurationInvalid}</p>}
      </div>
      <label className="checkbox-field temporary-access-pass-options__single-use">
        <input type="checkbox" checked={isUsableOnce} onChange={(event) => { setIsUsableOnce(event.target.checked); setError(null); }} />
        <span>{userFeatureMessages.userTapOneTimeUseLabel}</span>
      </label>
    </div>
    <UserWriteReasonField value={reason} onChange={(value) => { setReason(value); setReasonError(null); }} error={reasonError} />
    {error && <p role="alert">{error}</p>}
    {requestIdentifiers && <TechnicalDetails summary={userFeatureMessages.userTapRequestIdentifiers} items={[
      { label: userFeatureMessages.userTapGraphCorrelationId, value: requestIdentifiers.graphCorrelationId, copy: true },
      { label: userFeatureMessages.userTapGraphRequestId, value: requestIdentifiers.graphRequestId, copy: true },
    ]} />}
    {auditWarning && <p role="alert" className="audit-warning">{auditWarning}</p>}
  </ConfirmationDialog>;
}
