import React, { useState } from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { PermissionState } from '../../components/PermissionState';
import { messages } from '../../app/messages';
import { ResponsiveDataView } from '../../components/ResponsiveDataView';
import type { UserSummary as ApiUserSummary } from './usersApi';

export type UserSummary = ApiUserSummary;

export function UsersTable({
  users,
  capabilities,
  onNavigate,
  onDisable,
}: {
  users: UserSummary[];
  capabilities: CapabilityDecision[];
  onNavigate?: (path: string) => void;
  onDisable?: (user: UserSummary) => void;
}) {
  const [expanded, setExpanded] = useState<string[]>([]);
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');
  const showAccountStatus = updateDecision.state !== 'hidden' || disableDecision.state !== 'hidden';
  const showActions = disableDecision.state !== 'hidden';

  return (
    <ResponsiveDataView items={users} keyOf={user => user.id} label="Users"
      renderCompact={user => {
        const name = user.displayName || user.userPrincipalName || user.mail || messages.usersUnnamedUser;
        return <><strong>{name}</strong><dl className="responsive-data-view__details"><div><dt>{messages.usersUpnColumn}</dt><dd>{user.userPrincipalName || messages.usersUnavailableValue}</dd></div>{showAccountStatus && <div><dt>{messages.usersAccountStatusColumn}</dt><dd>{labelStatus(user.accountEnabled)}</dd></div>}<div><dt>{messages.usersMailColumn}</dt><dd>{user.mail || messages.usersUnavailableValue}</dd></div><div><dt>{messages.usersTypeColumn}</dt><dd>{user.userType || messages.usersUnavailableValue}</dd></div></dl><div className="responsive-data-view__actions"><button type="button" className="table-action" aria-label={`${messages.usersOpenAction} ${name}`} onClick={() => navigateToUser(user.id, onNavigate)}>{messages.usersOpenAction}</button>{disableDecision.state === 'allowed' && <PermissionState decision={disableDecision}><button type="button" className="table-action" aria-label={`${messages.usersDisableAction} ${name}`} disabled={!onDisable} onClick={() => onDisable?.(user)}>{messages.usersDisableAction}</button></PermissionState>}</div></>;
      }}
      renderTable={rows => <div className="users-table-wrap"><table className="users-table">
        <thead>
          <tr>
            <th scope="col">{messages.usersNameColumn}</th>
            <th scope="col">{messages.usersUpnColumn}</th>
            {showAccountStatus && <th scope="col">{messages.usersAccountStatusColumn}</th>}
            <th scope="col">{messages.usersOpenColumn}</th>
            {showActions && <th scope="col">{messages.usersActionsColumn}</th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((user) => {
            const displayName = user.displayName || user.userPrincipalName || user.mail || messages.usersUnnamedUser;
            return (
              <React.Fragment key={user.id}>
              <tr>
                <th scope="row" data-label={messages.usersNameColumn}><span className="users-name-cell"><strong>{displayName}</strong><button type="button" className="table-action" aria-label={`${expanded.includes(user.id) ? 'Less' : 'More'} details for ${displayName}`} aria-expanded={expanded.includes(user.id)} aria-controls={expanded.includes(user.id) ? `user-more-${user.id}` : undefined} onClick={() => setExpanded((current) => current.includes(user.id) ? current.filter((id) => id !== user.id) : [...current, user.id])}>{expanded.includes(user.id) ? 'Less' : 'More'}</button></span></th>
                <td data-label={messages.usersUpnColumn}>{user.userPrincipalName || messages.usersUnavailableValue}</td>
                {showAccountStatus && <td data-label={messages.usersAccountStatusColumn}>{labelStatus(user.accountEnabled)}</td>}
                <td data-label={messages.usersOpenColumn}>
                  <button type="button" className="table-action" onClick={() => navigateToUser(user.id, onNavigate)} aria-label={`${messages.usersOpenAction} ${displayName}`}>
                    {messages.usersOpenAction}
                  </button>
                </td>
                {showActions && (
                    <td data-label={messages.usersActionsColumn}>
                    <PermissionState decision={disableDecision}>
                      <button
                        type="button"
                        className="table-action"
                        aria-label={`${messages.usersDisableAction} ${displayName}`}
                        disabled={!onDisable}
                        title={!onDisable ? messages.usersDisableDeferredAction : undefined}
                        onClick={() => onDisable?.(user)}
                      >
                        {messages.usersDisableAction}
                      </button>
                    </PermissionState>
                  </td>
                )}
              </tr>
              {expanded.includes(user.id) && <tr id={`user-more-${user.id}`} className="users-table__detail"><td colSpan={3 + Number(showAccountStatus) + Number(showActions)}><dl><div><dt>{messages.usersMailColumn}</dt><dd>{user.mail || messages.usersUnavailableValue}</dd></div><div><dt>{messages.usersTypeColumn}</dt><dd>{user.userType || messages.usersUnavailableValue}</dd></div></dl></td></tr>}
              </React.Fragment>
            );
          })}
        </tbody>
      </table></div>}
    />
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
