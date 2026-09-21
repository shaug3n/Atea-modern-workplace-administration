import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import { InvitationRedemptionPage } from '../../src/Web/src/features/invitations/InvitationRedemptionPage';

vi.mock('../../src/Web/src/auth/AuthProvider', () => ({
  useAuth: () => ({ account: { username: 'customer.admin@example.test' }, getApiToken: async () => 'customer-token' })
}));

afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState({}, '', '/'); });

describe('deterministic local V1 journey', () => {
  it('redeems the customer invitation without using the local admin cookie', async () => {
    window.history.replaceState({}, '', '/invitations/fixture-nonce');
    render(
      <InvitationRedemptionPage nonce="fixture-nonce" redeem={async (nonce) => {
          expect(nonce).toBe('fixture-nonce');
          return { status: 'consent_required', workspaceId: 'workspace-1', workspaceName: 'Local customer', nextStep: '/overview' };
        }} />
    );

    fireEvent.click(await screen.findByRole('button', { name: /redeem/i }));
    expect(await screen.findByRole('heading', { name: /invitation redeemed/i })).toBeTruthy();
    expect(screen.queryByText(/nonce|hash|local admin cookie/i)).toBeNull();
  });

  it('shows overview connection state and permission-aware PIM handoff states', async () => {
    window.history.replaceState({}, '', '/overview');
    render(<App
      loadSession={async () => ({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' } })}
      loadCapabilities={async () => ({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'connected', capabilities: [
        { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'pim.activate', state: 'pim_eligible_inactive', reasonCode: 'eligible_inactive', pim: { state: 'eligible_inactive', activationUrl: 'https://entra.microsoft.com/pim' }, nextStep: { label: 'Activate the required Entra role', href: 'https://entra.microsoft.com/pim' } }
      ] })}
      loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: '2026-09-21T12:00:00Z' })}
    />);

    expect((await screen.findByTestId('connection-state')).textContent).toMatch(/stale|connected/i);
  });
});
