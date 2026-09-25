import { useCallback, useEffect, useState } from 'react';
import { useApi } from '../auth/useApi';
import type { CapabilitySnapshot } from './capabilityTypes';

export type CapabilityLoader = () => Promise<CapabilitySnapshot>;

export function useCapabilities(loadCapabilities?: CapabilityLoader) {
  const api = useApi();
  const [capabilities, setCapabilities] = useState<CapabilitySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [attempt, setAttempt] = useState(0);
  const refresh = useCallback(async () => { setAttempt(value => value + 1); }, []);

  useEffect(() => {
    let cancelled = false;
    const load = loadCapabilities ?? (async () => {
      const response = await api('/api/capabilities');
      if (!response.ok) {
        throw new Error('capabilities_unavailable');
      }
      return await response.json() as CapabilitySnapshot;
    });

    setLoading(true);
    setError(null);
    load()
      .then((snapshot) => {
        if (!cancelled) setCapabilities(snapshot);
      })
      .catch((loadError: unknown) => {
        if (!cancelled) setError(loadError instanceof Error ? loadError : new Error('capabilities_unavailable'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => { cancelled = true; };
  }, [api, loadCapabilities, attempt]);

  return { capabilities, loading, error, refresh };
}
