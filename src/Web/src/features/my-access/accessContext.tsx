import { createContext, useContext, type ReactNode } from 'react';
import type { AppSession } from '../../components/TenantContextHeader';
import { myAccessMessages } from './messages';
import type { CapabilitySnapshot } from '../../capabilities/capabilityTypes';

export type AccessTransparencyContextValue = {
  snapshot: CapabilitySnapshot | null;
  loading: boolean;
  error: boolean;
  refresh: () => Promise<void>;
};

const AccessTransparencyContext = createContext<AccessTransparencyContextValue | null>(null);

export function AccessTransparencyProvider({
  children,
  session,
  value,
}: {
  children: ReactNode;
  session: AppSession;
  value: AccessTransparencyContextValue;
}) {
  const scopedValue = value.snapshot?.workspaceId === session.workspace.id
    ? value
    : { ...value, snapshot: null };
  return <AccessTransparencyContext.Provider value={scopedValue}>{children}</AccessTransparencyContext.Provider>;
}

export function useAccessTransparency(): AccessTransparencyContextValue {
  const context = useContext(AccessTransparencyContext);
  if (!context) throw new Error(myAccessMessages.myAccessProviderRequired);
  return context;
}
