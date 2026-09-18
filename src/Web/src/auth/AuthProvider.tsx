import { InteractionRequiredAuthError, PublicClientApplication, type AccountInfo } from '@azure/msal-browser';
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react';
import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { apiScope, msalConfig } from './msalConfig';

const msalInstance = new PublicClientApplication(msalConfig);
type AuthContextValue = { account: AccountInfo | null; getApiToken: () => Promise<string>; signIn: () => Promise<void>; signOut: () => Promise<void> };
const AuthContext = createContext<AuthContextValue | null>(null);

function AuthenticatedContent({ children }: { children: ReactNode }) {
  const { instance, accounts } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const [error, setError] = useState<string | null>(null);
  const account = accounts[0] ?? null;
  const value = useMemo<AuthContextValue>(() => ({
    account,
    signIn: async () => { setError(null); await instance.loginRedirect({ scopes: [apiScope] }); },
    signOut: async () => { setError(null); await instance.logoutRedirect(); },
    getApiToken: async () => {
      if (!account) throw new Error('Sign-in is required');
      try {
        return (await instance.acquireTokenSilent({ account, scopes: [apiScope] })).accessToken;
      } catch (acquisitionError) {
        if (acquisitionError instanceof InteractionRequiredAuthError) {
          await instance.acquireTokenRedirect({ account, scopes: [apiScope] });
        }
        throw acquisitionError;
      }
    }
  }), [account, instance]);

  if (!isAuthenticated) return <main role="main"><h1>Sign in to Atea Unified Workplace</h1><button type="button" onClick={() => value.signIn().catch(() => setError('Sign-in could not be started.'))}>Sign in</button>{error && <p role="alert">{error}</p>}</main>;
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return <MsalProvider instance={msalInstance}><AuthenticatedContent>{children}</AuthenticatedContent></MsalProvider>;
}

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider');
  return context;
};
