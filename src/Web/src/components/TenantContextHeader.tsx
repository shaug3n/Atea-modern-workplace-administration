import React from 'react';
import { messages } from '../app/messages';
import { Icon } from './icons';

export type AppSession = {
  user: {
    objectId?: string | null;
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

function initialsOf(session: AppSession) {
  const name = (session.user.displayName ?? '').trim();
  if (!name) return (session.user.userPrincipalName ?? '?').trim().charAt(0).toUpperCase() || '?';
  const words = name.split(/\s+/).filter(Boolean);
  const letters = words.length > 1 ? `${words[0][0]}${words[1][0]}` : words[0][0];
  return letters.toUpperCase();
}

export function AccountSummary({ session }: { session: AppSession }) {
  const userLabel = session.user.displayName || session.user.userPrincipalName || messages.unknownUser;
  const shown = userLabel.length > 20 ? `${userLabel.slice(0, 19)}…` : userLabel;
  return (
    <div className="account-summary" aria-label={messages.signedInUserLabel} title={userLabel}>
      <span className="account-summary__avatar" aria-hidden="true">{initialsOf(session)}</span>
      <span className="account-summary__name">{shown}</span>
    </div>
  );
}

export function TenantContextHeader({ session, accessLimited = false, onAccessLimitedClick }: { session: AppSession; accessLimited?: boolean; onAccessLimitedClick?: () => void }) {
  return (
    <div className="tenant-context" aria-label={messages.tenantContextLabel}>
      <strong className="tenant-context__name" title={session.workspace.name}>{session.workspace.name}</strong>
      {accessLimited && (
        <button type="button" className="button button--sm button--secondary access-chip" onClick={onAccessLimitedClick}>
          <Icon name="lock" size={14} />
          <span>{messages.accessLimited}</span>
        </button>
      )}
    </div>
  );
}
