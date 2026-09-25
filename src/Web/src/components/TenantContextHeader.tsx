import React from 'react';
import { messages } from '../app/messages';
import { StatusBadge } from './StatusBadge';

export type AppSession = {
  user: {
    displayName?: string | null;
    userPrincipalName?: string | null;
  };
  workspace: {
    id: string;
    name: string;
    enabledModules?: string[];
    moduleAccess?: string[];
  };
  workspaceAccess?: {
    role: string;
    isOwner?: boolean;
    canManageMembers: boolean;
    canManageSettings: boolean;
    canManageModules?: boolean;
    canManageMemberModules?: boolean;
  };
};

export function TenantContextHeader({ session, capabilitySnapshotFresh }: { session: AppSession; capabilitySnapshotFresh?: boolean }) {
  const userLabel = session.user.displayName || session.user.userPrincipalName || messages.unknownUser;
  return (
    <div className="tenant-context" aria-label={messages.tenantContextLabel}>
      <div>
        <span className="tenant-context__label">{messages.workspaceLabel}</span>
        <strong>{session.workspace.name}</strong>
      </div>
      <StatusBadge tone={capabilitySnapshotFresh ? 'success' : 'warning'} label={capabilitySnapshotFresh ? messages.capabilitySnapshotFresh : messages.capabilitySnapshotUnavailable} detail={messages.capabilitySnapshotDetail} />
      <div className="user-menu" aria-label={messages.signedInUserLabel}>
        <span className="user-menu__label">{messages.signedInAs}</span>
        <strong>{userLabel}</strong>
      </div>
    </div>
  );
}
