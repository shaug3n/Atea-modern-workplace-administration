import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../src/Web/src/app/App';
import { InvitationLandingPage } from '../../../../src/Web/src/features/invitations/InvitationLandingPage';
import { PENDING_FLOW_KEY } from '../../../../src/Web/src/features/invitations/pendingFlow';

const auth = vi.hoisted(() => ({ signIn: vi.fn(), getApiToken: vi.fn() }));
const nonce = 'A'.repeat(43);
vi.mock('../../../../src/Web/src/auth/AuthProvider', () => ({
  useAuth: () => ({ account: null, signIn: auth.signIn, getApiToken: auth.getApiToken }),
}));

const scopes = [
  'User.Read', 'User.Read.All', 'Group.Read.All', 'Directory.Read.All', 'User.Create', 'User.ReadWrite.All',
  'User.EnableDisableAccount.All', 'User-PasswordProfile.ReadWrite.All', 'User.RevokeSessions.All',
  'GroupMember.ReadWrite.All', 'LicenseAssignment.ReadWrite.All', 'RoleManagement.Read.Directory',
  'RoleManagement.ReadWrite.Directory', 'DeviceManagementManagedDevices.Read.All',
  'DeviceManagementManagedDevices.ReadWrite.All', 'DeviceManagementManagedDevices.PrivilegedOperations.All',
  'BitlockerKey.ReadBasic.All', 'BitlockerKey.Read.All', 'DeviceLocalCredential.ReadBasic.All',
  'DeviceLocalCredential.Read.All', 'UserAuthenticationMethod.Read.All',
  'UserAuthenticationMethod.ReadWrite.All', 'MailboxSettings.Read',
];

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('InvitationLandingPage', () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  });

  beforeEach(() => {
    sessionStorage.clear();
    auth.signIn.mockReset();
    auth.getApiToken.mockReset();
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).endsWith('/preview')) {
        return jsonResponse({ workspaceName: 'Demo workspace', expiresAt: '2026-11-01T00:00:00Z', flow: 'consent_first', permissionScopes: scopes });
      }
      if (String(input).endsWith('/consent/start')) {
        return jsonResponse({
          authorizationUrl: 'https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0/adminconsent?state=signed-consent-state',
          scopes,
          challenge: 'signed-consent-state',
          correlationId: 'correlation-1',
          expiresAt: new Date(Date.now() + 60_000).toISOString(),
        });
      }
      return jsonResponse({}, 404);
    }));
  });

  it('renders the anonymous preview without an API token or workspace-session fetch', async () => {
    render(<InvitationLandingPage nonce={nonce} />);

    expect(await screen.findByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' })).toBeTruthy();
    expect(screen.getByText("An administrator approves Atea's delegated permissions in Microsoft. Microsoft adds the enterprise applications to your tenant. Then sign in with the account invited by Atea.")).toBeTruthy();
    expect(screen.getByText('Use an active Cloud Application Administrator, Application Administrator, Privileged Role Administrator or Global Administrator account. Your tenant may require MFA or PIM activation.')).toBeTruthy();
    expect(screen.getByText('Consent does not assign Microsoft 365 roles. Actions remain limited by your signed-in account, workspace access and tenant policy.')).toBeTruthy();
    expect(auth.getApiToken).not.toHaveBeenCalled();
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(String(vi.mocked(fetch).mock.calls[0][0])).toContain(`/api/invitations/${nonce}/preview`);
    const request = vi.mocked(fetch).mock.calls[0][1] as RequestInit;
    expect(new Headers(request.headers).get('Authorization')).toBeNull();
    expect(request.cache).toBe('no-store');
    expect(request.credentials).toBe('same-origin');
    expect(vi.mocked(fetch).mock.calls.some(([input]) => String(input) === '/api/session')).toBe(false);
  });

  it('renders the public invitation route before workspace session and capability loaders', async () => {
    const loadSession = vi.fn(async () => null as never);
    const loadCapabilities = vi.fn(async () => null as never);
    window.history.replaceState(null, '', `/invitations/${nonce}`);

    render(<App loadSession={loadSession} loadCapabilities={loadCapabilities} />);

    expect(await screen.findByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' })).toBeTruthy();
    expect(loadSession).not.toHaveBeenCalled();
    expect(loadCapabilities).not.toHaveBeenCalled();
    expect(auth.getApiToken).not.toHaveBeenCalled();
  });

  it('shows the exact delegated scope set in expandable details', async () => {
    render(<InvitationLandingPage nonce={nonce} />);
    await screen.findByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' });
    fireEvent.click(screen.getByText('Review delegated permissions'));

    for (const scope of scopes) expect(screen.getByText(scope)).toBeTruthy();
    expect(screen.getAllByRole('listitem')).toHaveLength(scopes.length);
  });

  it('stores the invitation challenge before redirecting to admin consent', async () => {
    const redirect = vi.fn(() => {
      const saved = JSON.parse(sessionStorage.getItem(PENDING_FLOW_KEY) ?? '{}');
      expect(saved).toMatchObject({ kind: 'invitation', nonce, challenge: 'signed-consent-state', step: 'consent_callback' });
    });
    render(<InvitationLandingPage nonce={nonce} redirectToConsent={redirect} />);
    await screen.findByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' });

    fireEvent.click(screen.getByRole('button', { name: 'Connect your Microsoft 365 tenant' }));

    await waitFor(() => expect(redirect).toHaveBeenCalledWith(expect.stringContaining('/v2.0/adminconsent')));
    const saved = JSON.parse(sessionStorage.getItem(PENDING_FLOW_KEY) ?? '{}');
    expect(saved).toMatchObject({ kind: 'invitation', nonce, challenge: 'signed-consent-state', step: 'consent_callback' });
    const startCall = vi.mocked(fetch).mock.calls.find(([input]) => String(input).endsWith('/consent/start'));
    expect(startCall).toBeTruthy();
    const startRequest = startCall?.[1] as RequestInit;
    expect(new Headers(startRequest.headers).get('Authorization')).toBeNull();
    expect(startRequest.body).toBe('{}');
  });

  it('presents regular sign-in for member invitations instead of anonymous consent', async () => {
    vi.mocked(fetch).mockImplementation(async (input: RequestInfo | URL) => {
      if (String(input).endsWith('/preview')) {
        return jsonResponse({ workspaceName: 'Demo workspace', expiresAt: '2026-11-01T00:00:00Z', flow: 'sign_in', permissionScopes: [] });
      }
      return jsonResponse({}, 404);
    });
    render(<InvitationLandingPage nonce={nonce} />);

    expect(await screen.findByRole('heading', { name: 'Join your workspace' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Sign in to continue' }));

    expect(auth.signIn).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Connect your Microsoft 365 tenant' })).toBeNull();
    expect(auth.getApiToken).not.toHaveBeenCalled();
  });

  it('shows the generic recovery copy when preview is unavailable', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse({ error: 'invitation_unavailable' }, 404));
    render(<InvitationLandingPage nonce={nonce} />);

    expect(await screen.findByRole('heading', { name: 'Your invitation is no longer available. Ask Atea for a new invitation.' })).toBeTruthy();
  });
});
