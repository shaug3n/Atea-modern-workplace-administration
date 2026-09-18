import { useEffect, useState } from 'react';
import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import { messages, type ConnectionState } from '../../messages/en';
import { useApi } from '../../auth/useApi';

type ConnectionHealth = { status: ConnectionState; lastVerifiedAt: string | null };
export type ConnectionHealthLoader = () => Promise<ConnectionHealth>;

export function OverviewPage({ loadConnectionHealth }: { loadConnectionHealth?: ConnectionHealthLoader }) {
  if (loadConnectionHealth) return <LoadedOverview loadConnectionHealth={loadConnectionHealth} />;
  return <AuthenticatedOverview />;
}

function AuthenticatedOverview() {
  const api = useApi();
  return <LoadedOverview loadConnectionHealth={async () => {
    const response = await api('/api/workspaces/current/connection-health');
    if (!response.ok) throw new Error('connection health request failed');
    return await response.json() as ConnectionHealth;
  }} />;
}

function LoadedOverview({ loadConnectionHealth }: { loadConnectionHealth: ConnectionHealthLoader }) {
  const [health, setHealth] = useState<ConnectionHealth | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    loadConnectionHealth().then(setHealth).catch(() => setFailed(true));
  }, [loadConnectionHealth]);
  if (failed) return <main><p role="alert">{messages.connectionUnavailable}</p></main>;
  if (!health) return <main><p>{messages.connectionLoading}</p></main>;
  return <main><ConnectionStatusCard state={health.status} lastVerifiedAt={health.lastVerifiedAt} /></main>;
}
