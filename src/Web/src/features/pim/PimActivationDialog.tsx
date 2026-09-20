import React, { useMemo, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import type { PimEligibility } from '../users/userDetailApi';
import { PimGuidedHandoff, type PimGuidedHandoffData } from './PimGuidedHandoff';

export type PimActivationResult = {
  status: 'active' | 'eligible_inactive' | 'not_eligible' | 'activation_pending' | 'approval_required' | 'mfa_required' | 'not_authorized' | 'temporarily_unavailable' | 'policy_blocked';
  requiredCapability: 'pim.activate';
  replayed: boolean;
  roleTemplateId?: string | null;
  roleDefinitionId?: string | null;
  displayName?: string | null;
  requestId?: string | null;
  error?: string | null;
  handoff?: PimGuidedHandoffData | null;
  graphCorrelationId?: string | null;
  graphRequestId?: string | null;
};

export function PimActivationDialog({
  eligibility,
  onClose,
  onCompleted,
}: {
  eligibility: PimEligibility;
  onClose?: () => void;
  onCompleted?: (result: PimActivationResult) => void;
}) {
  const api = useApi();
  const [durationMinutes, setDurationMinutes] = useState(Math.min(eligibility.maximumDurationMinutes ?? 60, 60));
  const [justification, setJustification] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<PimActivationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const roleName = eligibility.displayName || eligibility.roleTemplateId;
  const canRequest = eligibility.activationAvailable
    && eligibility.status === 'eligible_inactive'
    && confirmed
    && !busy
    && (!eligibility.requiresJustification || justification.trim().length > 0);
  const maxDuration = eligibility.maximumDurationMinutes ?? 480;
  const proposedChange = useMemo(
    () => messages.pimActivationProposedChange(roleName, durationMinutes),
    [durationMinutes, roleName],
  );

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const response = await api('/api/pim/activations', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': randomKey(),
        },
        body: JSON.stringify({
          roleTemplateId: eligibility.roleTemplateId,
          durationMinutes,
          justification: justification.trim() || null,
          confirmed: true,
          roleType: 'directoryRole',
        }),
      });
      const payload = await response.json().catch(() => ({})) as PimActivationResult;
      setResult(payload);
      onCompleted?.(payload);
      if (!response.ok && !payload.status) {
        setError(messages.pimActivationFailed);
      }
    } catch {
      setError(messages.pimActivationFailed);
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="pim-dialog-title">
      <h2 id="pim-dialog-title">{messages.pimActivationDialogTitle}</h2>
      <dl className="detail-list">
        <div>
          <dt>{messages.userMutationTarget}</dt>
          <dd>{roleName}</dd>
        </div>
        <div>
          <dt>{messages.userMutationProposedChange}</dt>
          <dd>{proposedChange}</dd>
        </div>
        <div>
          <dt>{messages.userRequiredCapability}</dt>
          <dd>pim.activate</dd>
        </div>
      </dl>
      <label>
        {messages.pimDurationLabel}
        <input
          aria-label={messages.pimDurationLabel}
          type="number"
          min={1}
          max={maxDuration}
          value={durationMinutes}
          onChange={(event) => setDurationMinutes(Number(event.target.value))}
        />
      </label>
      <label>
        {messages.pimJustificationLabel}
        <textarea
          aria-label={messages.pimJustificationLabel}
          value={justification}
          required={eligibility.requiresJustification}
          onChange={(event) => setJustification(event.target.value)}
        />
      </label>
      <div className="users-page__actions" aria-label={messages.pimPolicyRequirements}>
        {eligibility.requiresApproval && <span className="status-chip">{messages.userPimApprovalRequired}</span>}
        {eligibility.requiresMfa && <span className="status-chip">{messages.userPimMfaRequired}</span>}
        {eligibility.requiresJustification && <span className="status-chip">{messages.userPimJustificationRequired}</span>}
      </div>
      <label>
        <input type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} />
        {messages.pimActivationConfirmation}
      </label>
      {!eligibility.activationAvailable && <p role="alert">{messages.pimActivationUnavailable}</p>}
      {error && <p role="alert">{error}</p>}
      {result?.status === 'active' && <p role="status">{messages.pimActivationActive}</p>}
      {result?.status === 'activation_pending' && <p role="status">{messages.pimActivationPending}</p>}
      {result?.status && result.status !== 'active' && result.status !== 'activation_pending' && (
        <p role="alert">{messages.pimActivationStatus(result.status)}</p>
      )}
      {result?.handoff && <PimGuidedHandoff handoff={result.handoff} />}
      <div className="users-page__actions">
        {onClose && <button type="button" onClick={onClose}>{messages.userMutationCancel}</button>}
        <button type="button" disabled={!canRequest} onClick={submit}>{busy ? messages.userMutationSaving : messages.pimActivationSubmit}</button>
      </div>
    </section>
  );
}

function randomKey() {
  return globalThis.crypto?.randomUUID?.() ?? `pim-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
