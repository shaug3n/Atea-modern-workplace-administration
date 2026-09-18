import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import type { ConnectionState } from '../../messages/en';

export function OverviewPage({ state = 'awaiting_invitation' as ConnectionState }: { state?: ConnectionState }) {
  return <main><ConnectionStatusCard state={state} /></main>;
}
