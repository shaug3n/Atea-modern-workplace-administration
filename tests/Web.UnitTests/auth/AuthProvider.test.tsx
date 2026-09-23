import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import React from 'react';
import type { ReactNode } from 'react';

const auth = vi.hoisted(() => ({
  account: { homeAccountId: 'account-1', localAccountId: 'object-1', username: 'alex@example.com' },
  authenticated: true,
  inProgress: 'none',
  instance: {
    loginRedirect: vi.fn(),
    logoutRedirect: vi.fn(),
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn()
  }
}));

vi.mock('@azure/msal-browser', () => ({
  InteractionRequiredAuthError: class InteractionRequiredAuthError extends Error {},
  InteractionStatus: { None: 'none', Startup: 'startup' },
  PublicClientApplication: class PublicClientApplication {}
}));
vi.mock('@azure/msal-react', () => ({
  MsalProvider: ({ children }: { children: ReactNode }) => children,
  useMsal: () => ({ instance: auth.instance, accounts: auth.authenticated ? [auth.account] : [], inProgress: auth.inProgress }),
  useIsAuthenticated: () => auth.authenticated
}));

import { AuthProvider, useAuth } from '../../../src/Web/src/auth/AuthProvider';
import { msalConfig } from '../../../src/Web/src/auth/msalConfig';
import { useApi } from '../../../src/Web/src/auth/useApi';

function Harness() {
  const { signIn, signOut, getApiToken } = useAuth();
  const api = useApi();
  return <><button onClick={() => void signIn()}>sign-in</button><button onClick={() => void signOut()}>sign-out</button><button onClick={() => void getApiToken().catch(() => undefined)}>token</button><button onClick={() => void api('/api/session').catch(() => undefined)}>api</button></>;
}

describe('AuthProvider behavior', () => {
  afterEach(cleanup);

  beforeEach(() => {
    auth.authenticated = true;
    auth.inProgress = 'none';
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

  it('waits for MSAL initialization before enabling sign-in', () => {
    auth.authenticated = false;
    auth.inProgress = 'startup';
    render(<AuthProvider instance={auth.instance as never}><Harness /></AuthProvider>);
    expect((screen.getByRole('button', { name: 'Preparing sign-in…' }) as HTMLButtonElement).disabled).toBe(true);
    expect(auth.instance.loginRedirect).not.toHaveBeenCalled();
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
