import type { Configuration } from '@azure/msal-browser';

const requiredEnvironmentValue = (name: string, value: string | undefined): string => {
  if (!value) throw new Error(`${name} is required`);
  return value;
};

export const apiScope = requiredEnvironmentValue('VITE_ENTRA_API_SCOPE', import.meta.env.VITE_ENTRA_API_SCOPE);
export function tenantAuthority(tenantId: string): string {
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(tenantId) ||
    tenantId === '00000000-0000-0000-0000-000000000000'
  ) {
    throw new Error('tenant_authority_invalid');
  }
  return `https://login.microsoftonline.com/${tenantId}`;
}
const redirectUri = typeof window === 'undefined'
  ? requiredEnvironmentValue('VITE_ENTRA_REDIRECT_URI', import.meta.env.VITE_ENTRA_REDIRECT_URI)
  : `${window.location.origin}/auth/callback`;

export const msalConfig: Configuration = {
  auth: {
    clientId: requiredEnvironmentValue('VITE_ENTRA_CLIENT_ID', import.meta.env.VITE_ENTRA_CLIENT_ID),
    authority: requiredEnvironmentValue('VITE_ENTRA_AUTHORITY', import.meta.env.VITE_ENTRA_AUTHORITY),
    redirectUri
  },
  // Redirect sign-in reloads the page. Keep MSAL's transaction state for this
  // tab so the callback can be validated and completed after navigation.
  cache: { cacheLocation: 'sessionStorage' }
};
