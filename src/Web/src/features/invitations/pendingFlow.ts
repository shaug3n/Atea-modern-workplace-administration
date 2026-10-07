export const PENDING_FLOW_KEY = 'atea-unified-workplace:pending-consent-flow';

export type PendingFlowStep = 'consent_callback' | 'workspace_callback' | 'sign_in' | 'redeem' | 'complete' | 'result';

export type PendingInvitationFlow = {
  kind: 'invitation' | 'workspace';
  nonce?: string;
  challenge: string;
  tenantId?: string;
  expiresAt: string;
  step: PendingFlowStep;
};

function isValidFlow(value: unknown): value is PendingInvitationFlow {
  if (!value || typeof value !== 'object') return false;
  const flow = value as Partial<PendingInvitationFlow>;
  return (flow.kind === 'invitation' || flow.kind === 'workspace') &&
    typeof flow.challenge === 'string' && flow.challenge.length > 0 && flow.challenge.length <= 4096 &&
    typeof flow.expiresAt === 'string' && Number.isFinite(Date.parse(flow.expiresAt)) &&
    Date.parse(flow.expiresAt) > Date.now() &&
    ['consent_callback', 'workspace_callback', 'sign_in', 'redeem', 'complete', 'result'].includes(flow.step ?? '') &&
    (flow.kind !== 'invitation' || (typeof flow.nonce === 'string' && /^[A-Za-z0-9_-]{43}$/.test(flow.nonce))) &&
    (flow.tenantId === undefined ||
      (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(flow.tenantId) &&
        flow.tenantId !== '00000000-0000-0000-0000-000000000000'));
}

export function readPendingFlow(): PendingInvitationFlow | null {
  try {
    const value = sessionStorage.getItem(PENDING_FLOW_KEY);
    if (!value) return null;
    const parsed: unknown = JSON.parse(value);
    if (!isValidFlow(parsed)) {
      sessionStorage.removeItem(PENDING_FLOW_KEY);
      return null;
    }
    return parsed;
  } catch {
    sessionStorage.removeItem(PENDING_FLOW_KEY);
    return null;
  }
}

export function writePendingFlow(flow: PendingInvitationFlow): void {
  if (!isValidFlow(flow)) throw new Error('pending_flow_invalid');
  sessionStorage.setItem(PENDING_FLOW_KEY, JSON.stringify(flow));
}

export function updatePendingFlow(update: Partial<Pick<PendingInvitationFlow, 'tenantId' | 'step'>>): PendingInvitationFlow | null {
  const existing = readPendingFlow();
  if (!existing) return null;
  const updated = { ...existing, ...update };
  if (!isValidFlow(updated)) return null;
  writePendingFlow(updated);
  return updated;
}

export function hasPendingChallenge(challenge: string): boolean {
  return Boolean(challenge && readPendingFlow()?.challenge === challenge);
}

export function clearPendingFlow(): void {
  sessionStorage.removeItem(PENDING_FLOW_KEY);
}
