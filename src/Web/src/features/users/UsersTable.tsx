import React from 'react';
import type { CapabilityDecision } from '../../capabilities/capabilityTypes';
import { ActionMenu, type ActionMenuItem } from '../../components/ActionMenu';
import { StatusBadge } from '../../components/StatusBadge';
import { humanizeCapability } from '../../format/humanize';
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
  const updateDecision = findDecision(capabilities, 'users.update');
  const disableDecision = findDecision(capabilities, 'users.disable');
  const showAccountStatus = updateDecision.state !== 'hidden' || disableDecision.state !== 'hidden';
  const showActions = disableDecision.state !== 'hidden';
  const nameOf = (user: UserSummary) => user.displayName || user.userPrincipalName || user.mail || messages.usersUnnamedUser;
  const nameLink = (user: UserSummary) => <a className="cell-primary-link" href={`/users/${encodeURIComponent(user.id)}`} onClick={(event) => { event.preventDefault(); navigateToUser(user.id, onNavigate); }}>{nameOf(user)}</a>;
  const status = (user: UserSummary) => <StatusBadge tone={user.accountEnabled === true ? 'success' : 'neutral'} label={labelStatus(user.accountEnabled)} />;
  const menu = (user: UserSummary) => {
    if (!showActions) return null;
    const allowed = disableDecision.state === 'allowed';
    const items: ActionMenuItem[] = [
      { label: 'Open details', onSelect: () => navigateToUser(user.id, onNavigate) },
      {
        label: 'Disable user',
        danger: true,
        separatorBefore: true,
        disabled: !allowed || !onDisable,
        description: !allowed ? `Requires permission: ${humanizeCapability('users.disable')}` : !onDisable ? messages.usersDisableDeferredAction : undefined,
        onSelect: () => onDisable?.(user),
      },
    ];
    return <ActionMenu label="Actions" ariaLabel={`Actions for ${nameOf(user)}`} items={items} />;
  };

  return (
    <ResponsiveDataView items={users} keyOf={user => user.id} label="Users"
      renderCompact={user => <>
        <strong>{nameLink(user)}</strong>
        <dl className="responsive-data-view__details"><div><dt>{messages.usersUpnColumn}</dt><dd className="cell-secondary" title={user.userPrincipalName || undefined}>{user.userPrincipalName || messages.usersUnavailableValue}</dd></div>{showAccountStatus && <div><dt>{messages.usersAccountStatusColumn}</dt><dd>{status(user)}</dd></div>}<div><dt>{messages.usersMailColumn}</dt><dd>{user.mail || messages.usersUnavailableValue}</dd></div><div><dt>{messages.usersTypeColumn}</dt><dd>{user.userType || messages.usersUnavailableValue}</dd></div></dl>
        <div className="responsive-data-view__actions">{menu(user)}</div>
      </>}
      renderTable={rows => <div className="users-table-wrap"><table className="users-table">
        <thead>
          <tr>
            <th scope="col">{messages.usersNameColumn}</th>
            {showAccountStatus && <th scope="col">{messages.usersAccountStatusColumn}</th>}
            <th scope="col">{messages.usersTypeColumn}</th>
            {showActions && <th scope="col"><span className="sr-only">{messages.usersActionsColumn}</span></th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((user) => (
            <tr key={user.id}>
              <th scope="row" data-label={messages.usersNameColumn}><span className="users-name-cell">{nameLink(user)}{user.userPrincipalName && user.userPrincipalName !== nameOf(user) && <small className="cell-secondary" title={user.userPrincipalName}>{user.userPrincipalName}</small>}</span></th>
              {showAccountStatus && <td data-label={messages.usersAccountStatusColumn}>{status(user)}</td>}
              <td data-label={messages.usersTypeColumn}>{user.userType || messages.usersUnavailableValue}</td>
              {showActions && <td data-label={messages.usersActionsColumn}>{menu(user)}</td>}
            </tr>
          ))}
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
