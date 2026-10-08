import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useApi } from '../../auth/useApi';
import type { AppSession } from '../../components/TenantContextHeader';
import type { CapabilitySnapshot } from '../../capabilities/capabilityTypes';
import { myAccessMessages } from './messages';

export type AccessTransparencyContextValue = {
  snapshot: CapabilitySnapshot | null;
  loading: boolean;
  error: boolean;
  refresh: () => Promise<void>;
};

export type CapabilitySnapshotLoader = () => Promise<CapabilitySnapshot>;

const AccessTransparencyContext = createContext<AccessTransparencyContextValue | null>(null);

function ProviderState({
  children,
  session,
  loadSnapshot,
}: {
  children: ReactNode;
  session: AppSession;
  loadSnapshot: CapabilitySnapshotLoader;
}) {
  const [loadedSnapshot, setLoadedSnapshot] = useState<CapabilitySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const generation = useRef(0);

  const refresh = useCallback(async () => {
    const requestGeneration = ++generation.current;
    setLoading(true);
    setError(false);
    try {
      const nextSnapshot = await loadSnapshot();
      if (requestGeneration === generation.current) setLoadedSnapshot(nextSnapshot);
    } catch {
      if (requestGeneration === generation.current) setError(true);
    } finally {
      if (requestGeneration === generation.current) setLoading(false);
    }
  }, [loadSnapshot]);

  useEffect(() => {
    void refresh();
    return () => { generation.current += 1; };
  }, [refresh, session.workspace.id]);

  const snapshot = loadedSnapshot?.workspaceId === session.workspace.id ? loadedSnapshot : null;
  const value = useMemo(() => ({ snapshot, loading, error, refresh }), [snapshot, loading, error, refresh]);
  return <AccessTransparencyContext.Provider value={value}>{children}</AccessTransparencyContext.Provider>;
}

function ApiBackedProvider({ children, session }: { children: ReactNode; session: AppSession }) {
  const api = useApi();
  const loadSnapshot = useCallback(async () => {
    const response = await api('/api/capabilities');
    if (!response.ok) throw new Error(myAccessMessages.myAccessLoadFailed);
    return await response.json() as CapabilitySnapshot;
  }, [api]);
  return <ProviderState session={session} loadSnapshot={loadSnapshot}>{children}</ProviderState>;
}

export function AccessTransparencyProvider({
  children,
  session,
  loadSnapshot,
}: {
  children: ReactNode;
  session: AppSession;
  loadSnapshot?: CapabilitySnapshotLoader;
}) {
  return loadSnapshot
    ? <ProviderState session={session} loadSnapshot={loadSnapshot}>{children}</ProviderState>
    : <ApiBackedProvider session={session}>{children}</ApiBackedProvider>;
}

export function useAccessTransparency(): AccessTransparencyContextValue {
  const context = useContext(AccessTransparencyContext);
  if (!context) throw new Error(myAccessMessages.myAccessProviderRequired);
  return context;
}
