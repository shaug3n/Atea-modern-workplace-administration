import type { Configuration } from '@azure/msal-browser';

const requiredEnvironmentValue = (name: string): string => {
  const value = import.meta.env[name as keyof ImportMetaEnv] as string | undefined;
  if (!value) throw new Error(`${name} is required`);
  return value;
};

export const apiScope = requiredEnvironmentValue('VITE_ENTRA_API_SCOPE');

export const msalConfig: Configuration = {
  auth: {
    clientId: requiredEnvironmentValue('VITE_ENTRA_CLIENT_ID'),
    authority: requiredEnvironmentValue('VITE_ENTRA_AUTHORITY'),
    redirectUri: requiredEnvironmentValue('VITE_ENTRA_REDIRECT_URI')
  },
  cache: { cacheLocation: 'memoryStorage' }
};
