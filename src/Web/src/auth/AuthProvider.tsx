import { InteractionRequiredAuthError, InteractionStatus, PublicClientApplication, type AccountInfo } from '@azure/msal-browser';
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { messages } from '../app/messages';
import { apiScope, msalConfig } from './msalConfig';

const msalInstance = new PublicClientApplication(msalConfig);
type AuthContextValue = { account: AccountInfo | null; getApiToken: () => Promise<string>; signIn: () => Promise<void>; switchAccount: () => Promise<void>; signOut: () => Promise<void> };
const AuthContext = createContext<AuthContextValue | null>(null);

export function createAuthActions(instance: Pick<PublicClientApplication, 'loginRedirect' | 'logoutRedirect' | 'acquireTokenSilent' | 'acquireTokenRedirect'>, account: AccountInfo | null, setError: (error: string | null) => void): AuthContextValue {
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
    switchAccount: async () => {
      setError(null);
      await instance.logoutRedirect({ account: account ?? undefined, onRedirectNavigate: () => false });
      await instance.loginRedirect({ scopes: [apiScope], prompt: 'select_account' });
    },
    signOut: async () => { setError(null); await instance.logoutRedirect(); },
    getApiToken: async () => {
      if (!account) throw new Error(messages.authSignInRequired);
      try {
        return (await instance.acquireTokenSilent({ account, scopes: [apiScope] })).accessToken;
      } catch (acquisitionError) {
        if (acquisitionError instanceof InteractionRequiredAuthError) {
          await instance.acquireTokenRedirect({ account, scopes: [apiScope] });
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
  const account = accounts[0] ?? null;
  const value = useMemo(() => createAuthActions(instance, account, setError), [account, instance]);

  if (!isAuthenticated) {
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
