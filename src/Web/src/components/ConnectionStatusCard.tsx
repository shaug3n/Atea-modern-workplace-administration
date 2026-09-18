import { messages, type ConnectionState } from '../messages/en';

export function ConnectionStatusCard({ state, lastVerifiedAt, now, freshnessMs = 15 * 60 * 1000, onCheck, onConsent }: { state: ConnectionState; lastVerifiedAt?: string | null; now?: string; freshnessMs?: number; onCheck?: () => void; onConsent?: () => void }) {
  const verifiedAtMs = lastVerifiedAt ? Date.parse(lastVerifiedAt) : Number.NaN;
  const ageMs = new Date(now ?? Date.now()).getTime() - verifiedAtMs;
  const isStale = state === 'connected' && (!Number.isFinite(verifiedAtMs) || ageMs < 0 || ageMs > freshnessMs);
  const copy = messages.connectionState[state];
  const showConsent = state === 'consent_required' || state === 'consent_revoked';
  return <section aria-labelledby="connection-title" data-testid="connection-status-card" data-state={isStale ? 'stale' : state}>
    <h2 id="connection-title">{messages.connectionTitle}</h2>
    <p><strong data-testid="connection-state">{isStale ? messages.connectionStaleLabel : copy.label}</strong></p>
    <p>{isStale ? messages.connectionStaleAction : copy.action}</p>
    <div>
      {onCheck && <button type="button" onClick={onCheck}>{messages.connectionCheck}</button>}
      {showConsent && onConsent && <button type="button" onClick={onConsent}>{messages.connectionConsent}</button>}
    </div>
  </section>;
}
