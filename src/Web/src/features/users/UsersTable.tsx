import React from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { PermissionState } from '../../components/PermissionState';
import { messages } from '../../app/messages';
import type { UserSummary as ApiUserSummary } from './usersApi';

export type UserSummary = ApiUserSummary;

export function UsersTable({ users, capabilities, onNavigate }: { users: UserSummary[]; capabilities: CapabilityDecision[]; onNavigate?: (path: string) => void }) {
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');
  const showAccountStatus = updateDecision.state !== 'hidden' || disableDecision.state !== 'hidden';
  const showActions = disableDecision.state !== 'hidden';

  return (
    <div className="users-table-wrap" role="region" aria-label={messages.usersTableLabel} tabIndex={0}>
      <table className="users-table">
        <thead>
          <tr>
            <th scope="col">{messages.usersNameColumn}</th>
            <th scope="col">{messages.usersUpnColumn}</th>
            <th scope="col">{messages.usersMailColumn}</th>
            <th scope="col">{messages.usersTypeColumn}</th>
            {showAccountStatus && <th scope="col">{messages.usersAccountStatusColumn}</th>}
            <th scope="col">{messages.usersOpenColumn}</th>
            {showActions && <th scope="col">{messages.usersActionsColumn}</th>}
          </tr>
        </thead>
        <tbody>
          {users.map((user) => {
            const displayName = user.displayName || user.userPrincipalName || user.mail || messages.usersUnnamedUser;
            return (
              <tr key={user.id}>
                <th scope="row">{displayName}</th>
                <td>{user.userPrincipalName || messages.usersUnavailableValue}</td>
                <td>{user.mail || messages.usersUnavailableValue}</td>
                <td>{user.userType || messages.usersUnavailableValue}</td>
                {showAccountStatus && <td>{labelStatus(user.accountEnabled)}</td>}
                <td>
                  <button type="button" className="table-action" onClick={() => navigateToUser(user.id, onNavigate)} aria-label={`${messages.usersOpenAction} ${displayName}`}>
                    {messages.usersOpenAction}
                  </button>
                </td>
                {showActions && (
                  <td>
                    <PermissionState decision={disableDecision}>
                      <button type="button" className="table-action" aria-label={`${messages.usersDisableAction} ${displayName}`}>
                        {messages.usersDisableAction}
                      </button>
                    </PermissionState>
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function findDecision(capabilities: CapabilityDecision[], capability: CapabilityDecision['capability']): CapabilityDecision {
  return capabilities.find((decision) => decision.capability === capability) ?? { capability, state: 'hidden', reasonCode: 'capability_not_returned' };
}

function labelStatus(accountEnabled: boolean | null) {
  if (accountEnabled === true) return messages.usersStatusEnabled;
  if (accountEnabled === false) return messages.usersStatusDisabled;
  return messages.usersUnavailableValue;
}

function navigateToUser(userId: string, onNavigate?: (path: string) => void) {
  const path = `/users/${encodeURIComponent(userId)}`;
  if (onNavigate) {
    onNavigate(path);
    return;
  }

  window.history.pushState(null, '', path);
  window.dispatchEvent(new PopStateEvent('popstate'));
}
