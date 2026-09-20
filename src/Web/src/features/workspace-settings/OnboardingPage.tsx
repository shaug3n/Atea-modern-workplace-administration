import { ConnectionStatusCard } from '../../components/ConnectionStatusCard';
import type { ConnectionState } from '../../messages/en';

export function OnboardingPage({ state = 'awaiting_invitation' as ConnectionState }: { state?: ConnectionState }) {
  return <ConnectionStatusCard state={state} />;
}
