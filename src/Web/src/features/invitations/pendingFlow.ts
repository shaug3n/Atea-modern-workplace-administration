export const PENDING_FLOW_KEY = 'atea-unified-workplace:pending-consent-flow';

export type PendingFlowStep =
  | 'consent_callback'
  | 'consent_resume'
  | 'tenant_sign_in'
  | 'tenant_sign_in_started'
  | 'workspace_callback'
  | 'sign_in'
  | 'redeem'
  | 'redeeming'
  | 'completion_pending'
  | 'completion_submitted'
  | 'result';

export type PendingFlowResult = {
  status: string;
  availableScopes?: string[];
  missingScopes?: string[];
  unknownScopes?: string[];
};

export type PendingInvitationFlow = {
  kind: 'invitation' | 'workspace';
  nonce?: string;
  challenge: string;
  tenantId?: string;
  expiresAt: string;
  step: PendingFlowStep;
  result?: PendingFlowResult;
};

function isValidFlow(value: unknown): value is PendingInvitationFlow {
  if (!value || typeof value !== 'object') return false;
  const flow = value as Partial<PendingInvitationFlow>;
  return (flow.kind === 'invitation' || flow.kind === 'workspace') &&
    typeof flow.challenge === 'string' && flow.challenge.length > 0 && flow.challenge.length <= 4096 &&
    typeof flow.expiresAt === 'string' && Number.isFinite(Date.parse(flow.expiresAt)) &&
    Date.parse(flow.expiresAt) > Date.now() &&
    [
      'consent_callback', 'consent_resume', 'tenant_sign_in', 'tenant_sign_in_started',
      'workspace_callback', 'sign_in', 'redeem', 'redeeming', 'completion_pending',
      'completion_submitted', 'result',
    ].includes(flow.step ?? '') &&
    (flow.kind !== 'invitation' || (typeof flow.nonce === 'string' && /^[A-Za-z0-9_-]{43}$/.test(flow.nonce))) &&
    (flow.result === undefined || (
      typeof flow.result === 'object' &&
      typeof flow.result.status === 'string' &&
      ['availableScopes', 'missingScopes', 'unknownScopes'].every(key =>
        flow.result?.[key as keyof PendingFlowResult] === undefined ||
        (Array.isArray(flow.result?.[key as keyof PendingFlowResult]) &&
          (flow.result?.[key as keyof PendingFlowResult] as unknown[]).every(value => typeof value === 'string'))
      )
    )) &&
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

export function updatePendingFlow(update: Partial<Pick<PendingInvitationFlow, 'tenantId' | 'step' | 'result'>>): PendingInvitationFlow | null {
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
