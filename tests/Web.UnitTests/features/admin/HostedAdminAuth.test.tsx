import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const msal = vi.hoisted(() => {
  const account = { homeAccountId: 'account-1', localAccountId: 'operator-1', tenantId: 'atea-tenant', username: 'operator@atea.example', name: 'Atea Operator', environment: 'login.microsoftonline.com' };
  return {
    account,
    instance: {
      initialize: vi.fn(async () => undefined),
      handleRedirectPromise: vi.fn(async () => null),
      setActiveAccount: vi.fn(),
      getActiveAccount: vi.fn(() => null),
      getAllAccounts: vi.fn(() => [] as typeof account[]),
      acquireTokenSilent: vi.fn(async () => ({ accessToken: 'platform-access-token', account })),
      loginRedirect: vi.fn(async () => undefined),
      logoutRedirect: vi.fn(async () => undefined),
    },
  };
});

vi.mock('@azure/msal-browser', () => ({ PublicClientApplication: vi.fn(() => msal.instance) }));

import { HostedAdminAuth } from '../../../../src/Web/src/features/admin/HostedAdminAuth';

describe('HostedAdminAuth', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    msal.instance.getActiveAccount.mockReturnValue(null);
    msal.instance.getAllAccounts.mockReturnValue([]);
    msal.instance.handleRedirectPromise.mockResolvedValue(null);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ authenticated: true, tenantId: 'atea-tenant', objectId: 'operator-1', displayName: 'Atea Operator' }), { status: 200 })));
  });
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('starts a tenant-scoped Entra redirect to the dedicated admin callback', async () => {
    render(<HostedAdminAuth />);
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in with Microsoft Entra ID' }));

    expect(msal.instance.loginRedirect).toHaveBeenCalledWith({
      scopes: ['api://platform-api/access_as_user'],
      redirectUri: `${window.location.origin}/admin/auth/callback`,
    });
  });

  it('validates an existing Entra account with the platform API before showing the admin console', async () => {
    msal.instance.getAllAccounts.mockReturnValue([msal.account]);
    render(<HostedAdminAuth />);

    expect(await screen.findByRole('heading', { name: 'Atea platform administration' })).toBeTruthy();
    expect(fetch).toHaveBeenCalledWith('/api/platform/session', expect.objectContaining({
      headers: { Authorization: 'Bearer platform-access-token' },
      credentials: 'omit',
    }));
  });

  it('explains when the signed-in identity is not an Atea platform operator', async () => {
    msal.instance.getAllAccounts.mockReturnValue([msal.account]);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })));
    render(<HostedAdminAuth />);

    expect(await screen.findByRole('heading', { name: 'You are not authorized for platform administration' })).toBeTruthy();
    expect(screen.getByText(/not on the Atea platform operator allowlist/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Try again with Microsoft Entra ID' }));
    await waitFor(() => expect(msal.instance.logoutRedirect).toHaveBeenCalledWith({
      account: msal.account,
      onRedirectNavigate: expect.any(Function),
    }));
    expect(msal.instance.loginRedirect).toHaveBeenCalledWith({
      scopes: ['api://platform-api/access_as_user'],
      redirectUri: `${window.location.origin}/admin/auth/callback`,
      prompt: 'select_account',
    });
  });
});
