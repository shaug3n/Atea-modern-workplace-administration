import React, { useEffect, useState } from 'react';
import { PublicClientApplication, type AccountInfo, type AuthenticationResult } from '@azure/msal-browser';
import { ThemeProvider } from '../../components/ThemeToggle';
import { AdminShell } from './AdminShell';
import { adminAuthApi, type AdminSession } from './adminAuthApi';
import { setAdminPlatformTokenProvider } from './adminApi';

const required = (value: string | undefined) => value?.trim() || null;
const clientId = required(import.meta.env.VITE_PLATFORM_ADMIN_CLIENT_ID);
const authority = required(import.meta.env.VITE_PLATFORM_ADMIN_AUTHORITY);
const scope = required(import.meta.env.VITE_PLATFORM_ADMIN_SCOPE);
const redirectUri = typeof window === 'undefined'
  ? required(import.meta.env.VITE_PLATFORM_ADMIN_REDIRECT_URI)
  : `${window.location.origin}/admin/auth/callback`;

type ViewState = 'loading' | 'signed-out' | 'authenticated' | 'unauthorized' | 'forbidden' | 'unavailable' | 'misconfigured';

export function HostedAdminAuth() {
  const [state, setState] = useState<ViewState>('loading');
  const [session, setSession] = useState<AdminSession | null>(null);
  const [client, setClient] = useState<PublicClientApplication | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!clientId || !authority || !scope || !redirectUri) {
      setState('misconfigured');
      return;
    }
    let disposed = false;
    let removeTokenProvider: (() => void) | undefined;
    const app = new PublicClientApplication({ auth: { clientId, authority, redirectUri, postLogoutRedirectUri: `${window.location.origin}/admin` }, cache: { cacheLocation: 'sessionStorage' } });
    const authenticate = async (account: AccountInfo, redirectResult?: AuthenticationResult | null) => {
      app.setActiveAccount(account);
      const tokenResult = redirectResult?.account?.homeAccountId === account.homeAccountId
        ? redirectResult
        : await app.acquireTokenSilent({ account, scopes: [scope] });
      if (disposed) return;
      removeTokenProvider = setAdminPlatformTokenProvider(async () => {
        const result = await app.acquireTokenSilent({ account, scopes: [scope] });
        return result.accessToken;
      });
      try {
        const platformSession = await adminAuthApi.getPlatformSession(tokenResult.accessToken);
        if (!disposed) { setSession(platformSession); setState('authenticated'); }
      } catch (error) {
        if (!disposed) setState(error instanceof Error && error.message === 'hosted_admin_forbidden' ? 'forbidden' : error instanceof Error && error.message === 'hosted_admin_unauthorized' ? 'unauthorized' : 'unavailable');
      }
    };
    void (async () => {
      try {
        await app.initialize();
        const redirectResult = await app.handleRedirectPromise();
        if (disposed) return;
        setClient(app);
        const account = redirectResult?.account ?? app.getActiveAccount() ?? app.getAllAccounts()[0];
        if (!account) { setState('signed-out'); return; }
        await authenticate(account, redirectResult);
      } catch (error) {
        if (!disposed) setState(error instanceof Error && error.message === 'hosted_admin_forbidden' ? 'forbidden' : error instanceof Error && error.message === 'hosted_admin_unauthorized' ? 'unauthorized' : 'unavailable');
      }
    })();
    return () => { disposed = true; removeTokenProvider?.(); };
  }, []);

  const signIn = async () => {
    if (!client || !scope || !redirectUri) return;
    setBusy(true);
    try { await client.loginRedirect({ scopes: [scope], redirectUri }); }
    catch { setState('unavailable'); setBusy(false); }
  };
  const signOut = async () => {
    const account = client?.getActiveAccount();
    if (!client) return;
    await client.logoutRedirect({ account, postLogoutRedirectUri: `${window.location.origin}/admin` });
  };

  const message: Record<Exclude<ViewState, 'loading' | 'signed-out' | 'authenticated'>, { title: string; body: string }> = {
    misconfigured: { title: 'Admin sign-in is not configured', body: 'Set the platform admin client ID, tenant authority, API scope, and /admin/auth/callback redirect URI in the deployment configuration.' },
    unauthorized: { title: 'Sign-in could not be verified', body: 'Sign in again. If this keeps happening, ask an Atea platform administrator to check the sign-in and API audience configuration.' },
    forbidden: { title: 'You are not authorized for platform administration', body: 'Your Entra identity is signed in, but it is not on the Atea platform operator allowlist. Ask an Atea platform administrator to grant access.' },
    unavailable: { title: 'Platform sign-in is temporarily unavailable', body: 'Check your connection and try again. If the problem continues, contact the platform support team.' },
  };
  const errorState = state === 'misconfigured' || state === 'unauthorized' || state === 'forbidden' || state === 'unavailable';

  return <ThemeProvider>{state === 'authenticated' && session
    ? <AdminShell session={session} onSignOut={() => void signOut()} />
    : <main className="admin-login" aria-live="polite"><section className="admin-login__card">
      <h1>{state === 'loading' ? 'Checking your admin session…' : state === 'signed-out' ? 'Atea platform administration' : errorState ? message[state].title : ''}</h1>
      {state !== 'loading' && <p>{state === 'signed-out' ? 'Use your Atea Microsoft Entra account to continue.' : errorState ? message[state].body : ''}</p>}
      {state !== 'loading' && state !== 'misconfigured' && <button type="button" disabled={busy || !client} onClick={() => void signIn()}>{busy ? 'Redirecting…' : state === 'signed-out' ? 'Sign in with Microsoft Entra ID' : 'Try again with Microsoft Entra ID'}</button>}
    </section></main>}</ThemeProvider>;
}
