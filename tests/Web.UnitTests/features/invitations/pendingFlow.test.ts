import { describe, expect, it, beforeEach } from 'vitest';
import {
  clearPendingFlow,
  hasPendingChallenge,
  PENDING_FLOW_KEY,
  readPendingFlow,
  updatePendingFlow,
  writePendingFlow,
  type PendingInvitationFlow,
} from '../../../../src/Web/src/features/invitations/pendingFlow';

const nonce = 'A'.repeat(43);
const flow: PendingInvitationFlow = {
  kind: 'invitation',
  nonce,
  challenge: 'signed-consent-state',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  step: 'consent_callback',
};

describe('pending invitation flow', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it('stores and reads the pending transaction in this tab only', () => {
    writePendingFlow(flow);

    expect(sessionStorage.getItem(PENDING_FLOW_KEY)).toBe(JSON.stringify(flow));
    expect(readPendingFlow()).toEqual(flow);
  });

  it('matches only the exact expected challenge and clears the transaction', () => {
    writePendingFlow(flow);

    expect(hasPendingChallenge(flow.challenge)).toBe(true);
    expect(hasPendingChallenge(`${flow.challenge}-other`)).toBe(false);
    clearPendingFlow();
    expect(readPendingFlow()).toBeNull();
  });

  it('updates a pending step without replacing the signed challenge', () => {
    writePendingFlow(flow);

    const updated = updatePendingFlow({ step: 'sign_in', tenantId: '11111111-1111-1111-1111-111111111111' });

    expect(updated).toMatchObject({ challenge: flow.challenge, step: 'sign_in', tenantId: '11111111-1111-1111-1111-111111111111' });
  });

  it('clears an expired or malformed transaction', () => {
    sessionStorage.setItem(PENDING_FLOW_KEY, JSON.stringify({ ...flow, expiresAt: '2000-01-01T00:00:00Z' }));

    expect(readPendingFlow()).toBeNull();
    expect(sessionStorage.getItem(PENDING_FLOW_KEY)).toBeNull();

    sessionStorage.setItem(PENDING_FLOW_KEY, '{invalid json');
    expect(readPendingFlow()).toBeNull();
    expect(sessionStorage.getItem(PENDING_FLOW_KEY)).toBeNull();
  });
});
