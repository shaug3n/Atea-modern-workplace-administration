import { useCallback } from 'react';
import { useAuth } from './AuthProvider';

export function useApi() {
  const { getApiToken } = useAuth();
  return useCallback(async (path: string, init: RequestInit = {}) => {
    const token = await getApiToken();
    const headers = new Headers(init.headers);
    headers.set('Authorization', `Bearer ${token}`);
    return fetch(path, { ...init, headers });
  }, [getApiToken]);
}
