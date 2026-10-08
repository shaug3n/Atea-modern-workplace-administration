import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import React from 'react';
import type { ReactNode } from 'react';

const auth = vi.hoisted(() => ({
  account: { homeAccountId: 'account-1', localAccountId: 'object-1', tenantId: '22222222-2222-2222-2222-222222222222', username: 'alex@example.com' },
  accounts: [{ homeAccountId: 'account-1', localAccountId: 'object-1', tenantId: '22222222-2222-2222-2222-222222222222', username: 'alex@example.com' }],
  authenticated: true,
  inProgress: 'none',
  activeAccount: null as any,
  eventCallback: null as ((event: { eventType: string; payload?: unknown }) => void) | null,
  instance: {
    loginRedirect: vi.fn(),
    logoutRedirect: vi.fn(),
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn(),
    getActiveAccount: vi.fn(() => auth.activeAccount),
    setActiveAccount: vi.fn((account: unknown) => { auth.activeAccount = account; }),
    addEventCallback: vi.fn((callback: (event: { eventType: string; payload?: unknown }) => void) => {
      auth.eventCallback = callback;
      return 'callback-id';
    }),
    removeEventCallback: vi.fn(),
  }
}));

vi.mock('@azure/msal-browser', () => ({
  InteractionRequiredAuthError: class InteractionRequiredAuthError extends Error {},
  InteractionStatus: { None: 'none', Startup: 'startup' },
  EventType: { LOGIN_SUCCESS: 'msal:loginSuccess' },
  PublicClientApplication: class PublicClientApplication {}
}));
vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: ReactNode }) => children,
  useMsal: () => ({ instance: auth.instance, accounts: auth.authenticated ? auth.accounts : [], inProgress: auth.inProgress }),
  useIsAuthenticated: () => auth.authenticated
}));

import { AuthProvider, useAuth } from '../../../src/Web/src/auth/AuthProvider';
import { msalConfig } from '../../../src/Web/src/auth/msalConfig';
import { useApi } from '../../../src/Web/src/auth/useApi';
import { writePendingFlow } from '../../../src/Web/src/features/invitations/pendingFlow';

function Harness() {
  const { signIn, signInForTenant, switchAccount, signOut, getApiToken } = useAuth();
  const api = useApi();
  return <><button onClick={() => void signIn()}>sign-in</button><button onClick={() => void signInForTenant('11111111-1111-1111-1111-111111111111', '/invitations/nonce')}>tenant-sign-in</button><button onClick={() => void switchAccount()}>switch-account</button><button onClick={() => void signOut()}>sign-out</button><button onClick={() => void getApiToken().catch(() => undefined)}>token</button><button onClick={() => void getApiToken('11111111-1111-1111-1111-111111111111').catch(() => undefined)}>tenant-token</button><button onClick={() => void api('/api/session').catch(() => undefined)}>api</button></>;
}

describe('AuthProvider behavior', () => {
  afterEach(() => {
    cleanup();
    sessionStorage.clear();
    window.history.replaceState(null, '', '/');
  });

  beforeEach(() => {
    auth.authenticated = true;
    auth.inProgress = 'none';
    auth.accounts = [auth.account];
    auth.activeAccount = null;
    auth.eventCallback = null;
    vi.clearAllMocks();
    auth.instance.acquireTokenSilent.mockResolvedValue({ accessToken: 'api-token' });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}')));
  });

  it('signs in and signs out through MSAL redirects', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('sign-in'));
    fireEvent.click(screen.getByText('sign-out'));
    await waitFor(() => expect(auth.instance.loginRedirect).toHaveBeenCalledWith({ scopes: [expect.any(String)] }));
    expect(auth.instance.logoutRedirect).toHaveBeenCalledOnce();
  });

  it('supports switching accounts with an explicit account chooser prompt', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('switch-account'));
    await waitFor(() => expect(auth.instance.logoutRedirect).toHaveBeenCalledWith({
      account: auth.account,
      onRedirectNavigate: expect.any(Function),
    }));
    expect(auth.instance.loginRedirect).toHaveBeenCalledWith({ scopes: [expect.any(String)], prompt: 'select_account' });
  });

  it('waits for MSAL initialization before enabling sign-in', () => {
    auth.authenticated = false;
    auth.inProgress = 'startup';
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    expect((screen.getByRole('button', { name: 'Preparing sign-in…' }) as HTMLButtonElement).disabled).toBe(true);
    expect(auth.instance.loginRedirect).not.toHaveBeenCalled();
  });

  it('renders invitation routes anonymously without entering the sign-in guard', () => {
    auth.authenticated = false;
    window.history.replaceState(null, '', `/invitations/${'A'.repeat(43)}`);

    render(<AuthProvider instance={auth.instance as never}><p>public invitation surface</p></AuthProvider>);

    expect(screen.getByText('public invitation surface')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
    expect(auth.instance.acquireTokenSilent).not.toHaveBeenCalled();
  });

  it('renders consent callbacks anonymously so callback dispatch can validate tab state', () => {
    auth.authenticated = false;
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=callback-secret');
    render(<AuthProvider instance={auth.instance as never}><p>consent callback surface</p></AuthProvider>);
    expect(screen.getByText('consent callback surface')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull();
  });

  it('uses the server tenant authority and explicit account selection for consent sign-in', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('tenant-sign-in'));
    await waitFor(() => expect(auth.instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({
      authority: 'https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111',
      prompt: 'select_account',
      redirectStartPage: `${window.location.origin}/invitations/nonce`,
    })));
  });

  it('does not acquire an API token using a cached account from another tenant', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('tenant-token'));
    await waitFor(() => expect(auth.instance.acquireTokenSilent).not.toHaveBeenCalled());
    expect(auth.instance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('selects only the cached account matching the tenant stored in the pending invitation', async () => {
    const tenantId = '11111111-1111-1111-1111-111111111111';
    const matchingAccount = { homeAccountId: 'target-account', tenantId, username: 'invited@example.com' };
    auth.accounts = [auth.account, matchingAccount];
    writePendingFlow({
      kind: 'invitation',
      nonce: 'A'.repeat(43),
      challenge: 'signed-challenge',
      tenantId,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      step: 'tenant_sign_in_started',
    });
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);

    fireEvent.click(screen.getByText('tenant-token'));

    await waitFor(() => expect(auth.instance.acquireTokenSilent).toHaveBeenCalledWith({
      account: matchingAccount,
      scopes: [expect.any(String)],
    }));
  });

  it('does not silently choose the first of multiple same-tenant accounts and offers tenant-pinned selection', async () => {
    const tenantId = '11111111-1111-1111-1111-111111111111';
    auth.accounts = [
      { homeAccountId: 'first-account', localAccountId: 'first', tenantId, username: 'first@example.com' },
      { homeAccountId: 'second-account', localAccountId: 'second', tenantId, username: 'second@example.com' },
    ];
    writePendingFlow({
      kind: 'invitation', nonce: 'A'.repeat(43), challenge: 'signed-challenge', tenantId,
      expiresAt: new Date(Date.now() + 60_000).toISOString(), step: 'tenant_sign_in_started',
    });
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);

    expect(auth.instance.acquireTokenSilent).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Choose account' }));

    await waitFor(() => expect(auth.instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({
      authority: 'https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111',
      prompt: 'select_account',
      redirectStartPage: window.location.href,
    })));
  });

  it('sets the account returned by the MSAL LOGIN_SUCCESS event as active', async () => {
    const selectedAccount = { homeAccountId: 'selected-account', localAccountId: 'selected', tenantId: auth.account.tenantId, username: 'selected@example.com' };
    auth.accounts = [auth.account, selectedAccount];
    const rendered = render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    await waitFor(() => expect(auth.instance.addEventCallback).toHaveBeenCalledOnce());

    auth.eventCallback?.({ eventType: 'msal:loginSuccess', payload: { account: selectedAccount } });
    expect(auth.instance.setActiveAccount).toHaveBeenCalledWith(selectedAccount);
    rendered.rerender(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('token'));

    await waitFor(() => expect(auth.instance.acquireTokenSilent).toHaveBeenCalledWith({
      account: selectedAccount,
      scopes: [expect.any(String)],
    }));
  });

  it('does not begin a second sign-in while MSAL handles a redirect', () => {
    auth.authenticated = false;
    auth.inProgress = 'handleRedirect';
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    expect((screen.getByRole('button', { name: 'Sign in' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('uses tab-scoped storage so redirect authentication can complete after navigation', () => {
    expect(msalConfig.cache?.cacheLocation).toBe('sessionStorage');
  });

  it('uses the current host for the Entra callback so labelled ACA revisions return to that revision', () => {
    expect(msalConfig.auth.redirectUri).toBe(`${window.location.origin}/auth/callback`);
  });

  it('shows an accessible sign-in error when redirect cannot start', async () => {
    auth.authenticated = false;
    const signInFailure = Object.assign(new Error('redirect failed'), { requestId: 'request-secret', correlationId: 'correlation-secret' });
    auth.instance.loginRedirect.mockRejectedValueOnce(signInFailure);
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect((await screen.findByRole('alert')).textContent).toBe('Sign-in could not be started.');
    expect(errorSpy).toHaveBeenCalledWith('MSAL sign-in failed');
    expect(errorSpy.mock.calls.flat().join(' ')).not.toContain('request-secret');
    expect(errorSpy.mock.calls.flat().join(' ')).not.toContain('correlation-secret');
  });

  it('acquires the API token silently for authenticated requests', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('token'));
    await waitFor(() => expect(auth.instance.acquireTokenSilent).toHaveBeenCalledWith({ account: auth.account, scopes: [expect.any(String)] }));
  });

  it('redirects when silent acquisition requires interaction', async () => {
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    auth.instance.acquireTokenSilent.mockRejectedValue(new InteractionRequiredAuthError());
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('token'));
    await waitFor(() => expect(auth.instance.acquireTokenRedirect).toHaveBeenCalledWith({ account: auth.account, scopes: [expect.any(String)] }));
  });

  it('adds only the API bearer token to API requests', async () => {
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    fireEvent.click(screen.getByText('api'));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/session', expect.objectContaining({ headers: expect.any(Headers) })));
    const request = vi.mocked(fetch).mock.calls[0][1] as RequestInit;
    expect((request.headers as Headers).get('Authorization')).toBe('Bearer api-token');
  });
});
