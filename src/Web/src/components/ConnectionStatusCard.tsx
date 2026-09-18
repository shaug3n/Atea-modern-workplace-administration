import { messages, type ConnectionState } from '../messages/en';

export function ConnectionStatusCard({ state, onCheck, onConsent }: { state: ConnectionState; onCheck?: () => void; onConsent?: () => void }) {
  const copy = messages.connectionState[state];
  const showConsent = state === 'consent_required' || state === 'consent_revoked';
  return <section aria-labelledby="connection-title" data-testid="connection-status-card">
    <h2 id="connection-title">{messages.connectionTitle}</h2>
    <p><strong data-testid="connection-state">{copy.label}</strong></p>
    <p>{copy.action}</p>
    <div>
      {onCheck && <button type="button" onClick={onCheck}>{messages.connectionCheck}</button>}
      {showConsent && onConsent && <button type="button" onClick={onConsent}>{messages.connectionConsent}</button>}
    </div>
  </section>;
}
