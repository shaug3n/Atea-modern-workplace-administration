import { messages, type ConnectionState } from '../messages/en';

export function ConnectionStatusCard({ state, lastVerifiedAt, now, freshnessMs = 15 * 60 * 1000, onCheck, onConsent, consentUrl, actionPending = false, actionError }: { state: ConnectionState; lastVerifiedAt?: string | null; now?: string; freshnessMs?: number; onCheck?: () => void; onConsent?: () => void; consentUrl?: string | null; actionPending?: boolean; actionError?: string | null }) {
  const verifiedAtMs = lastVerifiedAt ? Date.parse(lastVerifiedAt) : Number.NaN;
  const ageMs = new Date(now ?? Date.now()).getTime() - verifiedAtMs;
  const isStale = state === 'connected' && (!Number.isFinite(verifiedAtMs) || ageMs < 0 || ageMs > freshnessMs);
  const copy = messages.connectionState[state];
  const showConsent = state === 'consent_required' || state === 'consent_revoked' || state === 'connection_failed';
  return <section aria-labelledby="connection-title" data-testid="connection-status-card" data-state={isStale ? 'stale' : state}>
    <h2 id="connection-title">{messages.connectionTitle}</h2>
    <p><strong data-testid="connection-state">{isStale ? messages.connectionStaleLabel : copy.label}</strong></p>
    <p>{isStale ? messages.connectionStaleAction : copy.action}</p>
    <div>
      {onCheck && <button type="button" onClick={onCheck} disabled={actionPending}>{messages.connectionCheck}</button>}
      {showConsent && onConsent && <button type="button" onClick={onConsent} disabled={actionPending}>{messages.connectionConsent}</button>}
      {consentUrl && <a href={consentUrl}>{messages.connectionContinueConsent}</a>}
    </div>
    {actionError && <p role="alert">{actionError}</p>}
  </section>;
}
