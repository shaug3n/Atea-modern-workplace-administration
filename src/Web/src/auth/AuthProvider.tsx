import { EventType, InteractionRequiredAuthError, InteractionStatus, PublicClientApplication, type AccountInfo, type AuthenticationResult } from '@azure/msal-browser';
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { messages } from '../app/messages';
import { apiScope, msalConfig, tenantAuthority } from './msalConfig';
import { readPendingFlow } from '../features/invitations/pendingFlow';

const msalInstance = new PublicClientApplication(msalConfig);
type AuthContextValue = { account: AccountInfo | null; getApiToken: (expectedTenantId?: string) => Promise<string>; signIn: () => Promise<void>; signInForTenant: (tenantId: string, returnPath: string) => Promise<void>; selectAccount: (tenantId?: string) => Promise<void>; switchAccount: () => Promise<void>; signOut: () => Promise<void> };
const AuthContext = createContext<AuthContextValue | null>(null);

export function createAuthActions(
  instance: Pick<PublicClientApplication, 'loginRedirect' | 'logoutRedirect' | 'acquireTokenSilent' | 'acquireTokenRedirect'>,
  account: AccountInfo | null,
  setError: (error: string | null) => void,
  accounts: AccountInfo[] = account ? [account] : [],
): AuthContextValue {
  const accountForTenant = (expectedTenantId?: string) => {
    if (!expectedTenantId) return account;
    const matching = accounts.filter(candidate => candidate.tenantId.toLowerCase() === expectedTenantId.toLowerCase());
    if (account?.tenantId.toLowerCase() === expectedTenantId.toLowerCase()) return account;
    return matching.length === 1 ? matching[0] : null;
  };
  return {
    account,
    signIn: async () => {
      setError(null);
      const request: Parameters<typeof instance.loginRedirect>[0] = { scopes: [apiScope] };
      if (/^\/invitations\/[^/]+$/.test(window.location.pathname)) {
        request.redirectStartPage = window.location.href;
        request.state = window.crypto.randomUUID();
      }
      await instance.loginRedirect(request);
    },
    signInForTenant: async (tenantId, returnPath) => {
      if (!returnPath.startsWith('/') || returnPath.startsWith('//')) {
        throw new Error('tenant_sign_in_invalid');
      }
      const returnUrl = new URL(returnPath, window.location.origin);
      if (returnUrl.origin !== window.location.origin) throw new Error('tenant_sign_in_invalid');
      await instance.loginRedirect({
        scopes: [apiScope],
        authority: tenantAuthority(tenantId),
        prompt: 'select_account',
        redirectStartPage: returnUrl.href,
      });
    },
    selectAccount: async (tenantId) => {
      await instance.loginRedirect({
        scopes: [apiScope],
        prompt: 'select_account',
        redirectStartPage: window.location.href,
        ...(tenantId ? { authority: tenantAuthority(tenantId) } : {}),
      });
    },
    switchAccount: async () => {
      setError(null);
      await instance.logoutRedirect({ account: account ?? undefined, onRedirectNavigate: () => false });
      await instance.loginRedirect({ scopes: [apiScope], prompt: 'select_account' });
    },
    signOut: async () => { setError(null); await instance.logoutRedirect(); },
    getApiToken: async (expectedTenantId) => {
      const selectedAccount = accountForTenant(expectedTenantId);
      if (!selectedAccount) throw new Error(messages.authSignInRequired);
      try {
        return (await instance.acquireTokenSilent({ account: selectedAccount, scopes: [apiScope] })).accessToken;
      } catch (acquisitionError) {
        if (acquisitionError instanceof InteractionRequiredAuthError) {
          await instance.acquireTokenRedirect({ account: selectedAccount, scopes: [apiScope] });
        }
        throw acquisitionError;
      }
    }
  };
}

function AuthenticatedContent({ children }: { children: ReactNode }) {
  const { instance, accounts, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    const callbackId = instance.addEventCallback((message) => {
      if (message.eventType !== EventType.LOGIN_SUCCESS) return;
      const account = (message.payload as AuthenticationResult | null)?.account;
      if (account) instance.setActiveAccount(account);
    });
    return () => {
      if (callbackId) instance.removeEventCallback(callbackId);
    };
  }, [instance]);
  const activeAccount = typeof instance.getActiveAccount === 'function' ? instance.getActiveAccount() : null;
  const pendingFlow = readPendingFlow();
  const expectedTenantId = pendingFlow?.tenantId;
  const matchingAccounts = expectedTenantId
    ? accounts.filter(candidate => candidate.tenantId.toLowerCase() === expectedTenantId.toLowerCase())
    : [];
  const account = activeAccount && (!expectedTenantId || activeAccount.tenantId.toLowerCase() === expectedTenantId.toLowerCase())
    ? activeAccount
    : expectedTenantId && matchingAccounts.length === 1
      ? matchingAccounts[0]
      : !expectedTenantId && accounts.length === 1 ? accounts[0] : null;
  const value = useMemo(() => createAuthActions(instance, account, setError, accounts), [account, accounts, instance]);
  const isPublicInvitationRoute = /^\/invitations\/[^/]+$/.test(window.location.pathname);
  const isConsentCallbackRoute = ['/onboarding/consent/callback', '/consent-callback'].includes(window.location.pathname) &&
    readPendingFlow()?.kind !== 'workspace';

  if (isAuthenticated && !account && accounts.length > 0) {
    return <main role="main"><h1>Choose an account to continue</h1><button type="button" onClick={() => value.selectAccount(expectedTenantId).catch(() => { console.error('MSAL account selection failed'); setError(messages.authSignInError); })}>{messages.authChooseAccount}</button>{error && <p role="alert">{error}</p>}</main>;
  }
  if (!isAuthenticated && !isPublicInvitationRoute && !isConsentCallbackRoute) {
    const signInInProgress = inProgress !== InteractionStatus.None;
    return <main role="main"><h1>{messages.authSignInTitle}</h1><button type="button" disabled={signInInProgress} onClick={() => value.signIn().catch(() => { console.error('MSAL sign-in failed'); setError(messages.authSignInError); })}>{inProgress === InteractionStatus.Startup ? messages.authPreparing : messages.authSignIn}</button>{error && <p role="alert">{error}</p>}</main>;
  }
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function AuthProvider({ children, instance = msalInstance }: { children: ReactNode; instance?: PublicClientApplication }) {
  return <MsalProvider instance={instance}><AuthenticatedContent>{children}</AuthenticatedContent></MsalProvider>;
}

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
};
