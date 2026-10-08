import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import React, { Profiler, StrictMode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { readPendingFlow, writePendingFlow } from '../../../../src/Web/src/features/invitations/pendingFlow';
import { InvitationRedemptionPage } from '../../../../src/Web/src/features/invitations/InvitationRedemptionPage';

const auth = vi.hoisted(() => ({
  account: { username: 'customer@example.com', tenantId: '11111111-1111-1111-1111-111111111111' },
  getApiToken: vi.fn().mockResolvedValue('token'),
  signInForTenant: vi.fn(),
}));
vi.mock('../../../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => auth }));

describe('InvitationRedemptionPage', () => {
  afterEach(() => {
    window.sessionStorage.clear();
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it('redeems without rendering the nonce and navigates to overview', async () => {
    const redeem = vi.fn().mockResolvedValue({ status: 'consent_required', workspaceId: 'w-1', workspaceName: 'Demo', nextStep: '/overview' });
    render(<InvitationRedemptionPage nonce="secret-nonce" redeem={redeem} />);
    expect(screen.getByText('customer@example.com')).toBeTruthy();
    expect(screen.queryByText('secret-nonce')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Redeem invitation' }));
    expect(await screen.findByText('Demo')).toBeTruthy();
    expect(redeem).toHaveBeenCalledWith('secret-nonce');
  });

  it('redeems and completes the consent-first transaction once under StrictMode', async () => {
    const nonce = 'A'.repeat(43);
    const challenge = 'signed-challenge';
    const tenantId = '11111111-1111-1111-1111-111111111111';
    writePendingFlow({
      kind: 'invitation',
      nonce,
      challenge,
      tenantId,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      step: 'tenant_sign_in_started',
    });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'consent_required', workspaceId: 'workspace-1', workspaceName: 'Demo', nextStep: '/overview',
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        valid: true,
        status: 'consent_received',
        correlationId: 'correlation',
        health: { status: 'connected', lastVerifiedAt: new Date().toISOString(), permissionCoverage: { availableScopes: ['User.Read'], missingScopes: [], unknownScopes: [] } },
      }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    render(<StrictMode><InvitationRedemptionPage nonce={nonce} challenge={challenge} tenantId={tenantId} /></StrictMode>);

    expect(await screen.findByText('Connected to Microsoft 365')).toBeTruthy();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      `/api/invitations/${nonce}/redeem`,
      '/api/workspaces/current/consent/complete',
    ]);
    const redemptionRequest = fetchMock.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(String(redemptionRequest.body))).toEqual({ challenge });
    expect(new Headers(redemptionRequest.headers).get('Content-Type')).toBe('application/json');
    expect(auth.getApiToken).toHaveBeenCalledWith(tenantId);
    expect(readPendingFlow()?.step).toBe('result');
  });

  it('checks health after an uncertain completion without replaying consumed consent state', async () => {
    const nonce = 'B'.repeat(43);
    const challenge = 'signed-challenge';
    const tenantId = '11111111-1111-1111-1111-111111111111';
    writePendingFlow({
      kind: 'invitation', nonce, challenge, tenantId,
      expiresAt: new Date(Date.now() + 60_000).toISOString(), step: 'tenant_sign_in_started',
    });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'consent_required', workspaceId: 'workspace-1', workspaceName: 'Demo', nextStep: '/overview',
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{}', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        status: 'temporarily_unavailable',
        permissionCoverage: { availableScopes: [], missingScopes: [], unknownScopes: ['User.Read.All'] },
      }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    let commits = 0;

    render(
      <StrictMode>
        <Profiler id="invitation-redemption" onRender={() => { commits += 1; }}>
          <InvitationRedemptionPage nonce={nonce} challenge={challenge} tenantId={tenantId} />
        </Profiler>
      </StrictMode>,
    );

    expect(await screen.findByText(/Connection coverage could not be verified/)).toBeTruthy();
    await new Promise(resolve => setTimeout(resolve, 250));
    expect(commits).toBeLessThan(12);
    expect(readPendingFlow()?.step).toBe('completion_submitted');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    await waitFor(() => expect(readPendingFlow()?.step).toBe('result'));
    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      `/api/invitations/${nonce}/redeem`,
      '/api/workspaces/current/consent/complete',
      '/api/workspaces/current/connection-health/check',
    ]);
  });
});
