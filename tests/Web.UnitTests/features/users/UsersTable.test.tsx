import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../../../src/Web/src/capabilities/capabilityTypes';
import { UsersTable, type UserSummary } from '../../../../src/Web/src/features/users/UsersTable';

const users: UserSummary[] = [
  { id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: 'ada@example.com', accountEnabled: true, userType: 'Member' },
];

function decision(capability: CapabilityDecision['capability'], state: CapabilityDecision['state']): CapabilityDecision {
  return { capability, state, reasonCode: state === 'allowed' ? 'active_role' : 'directory_role_required' };
}

describe('UsersTable', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('gives compact readers the user identifier and open action without a mutation control', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<UsersTable users={[{ ...users[0], userPrincipalName: 'a'.repeat(90) + '@example.com' }]} capabilities={[decision('users.update', 'hidden'), decision('users.disable', 'hidden')]} />);
    const compact = screen.getByRole('list', { name: 'Users' });
    expect(compact.textContent).toContain('a'.repeat(90) + '@example.com');
    expect(compact.querySelector('button[aria-label="Open Ada Lovelace"]')).toBeTruthy();
    expect(compact.querySelector('button[aria-label="Disable Ada Lovelace"]')).toBeNull();
    vi.unstubAllGlobals();
  });

  it('omits compact mutation controls for a read-only disable capability', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<UsersTable users={users} capabilities={[decision('users.update', 'read_only'), decision('users.disable', 'read_only')]} />);
    const compact = screen.getByRole('list', { name: 'Users' });
    expect(compact.textContent).toContain('Ada Lovelace');
    expect(compact.textContent).toContain('Enabled');
    expect(screen.getByRole('button', { name: 'Open Ada Lovelace' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Disable Ada Lovelace' })).toBeNull();
    vi.unstubAllGlobals();
  });

  it('keeps primary columns compact and reveals mail and type on demand', () => {
    render(<UsersTable users={users} capabilities={[decision('users.update', 'allowed')]} />);
    expect(screen.queryByRole('columnheader', { name: 'Mail' })).toBeNull();
    expect(screen.queryByRole('columnheader', { name: 'User type' })).toBeNull();
    const expand = screen.getByRole('button', { name: 'More details for Ada Lovelace' });
    expect(expand.getAttribute('aria-expanded')).toBe('false');
    fireEvent.click(expand);
    expect(expand.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByText('Mail')).toBeTruthy();
    expect(screen.getByText('Member')).toBeTruthy();
  });

  it('hides the account status column when the related capability is hidden', () => {
    render(
      <UsersTable
        users={users}
        capabilities={[
          decision('users.update', 'hidden'),
          decision('users.disable', 'hidden'),
          decision('users.create', 'hidden'),
        ]}
      />
    );

    expect(screen.queryByRole('columnheader', { name: 'Account status' })).toBeNull();
    expect(screen.queryByText('Enabled')).toBeNull();
  });

  it('keeps mutation controls visible but unavailable when capability is read only', () => {
    render(
      <UsersTable
        users={users}
        capabilities={[
          decision('users.update', 'read_only'),
          decision('users.disable', 'read_only'),
          decision('users.create', 'hidden'),
        ]}
      />
    );

    expect(screen.getByRole('columnheader', { name: 'Account status' })).toBeTruthy();
    expect(screen.getByText('Enabled')).toBeTruthy();
    const disableButton = screen.getByRole('button', { name: 'Disable Ada Lovelace' }) as HTMLButtonElement;
    expect(disableButton.disabled).toBe(true);
    expect(screen.getByRole('status').textContent).toContain('read-only');
  });

  it('navigates to the user detail route when a row is opened', () => {
    const onNavigate = vi.fn();
    render(
      <UsersTable
        users={users}
        capabilities={[
          decision('users.update', 'allowed'),
          decision('users.disable', 'allowed'),
          decision('users.create', 'allowed'),
        ]}
        onNavigate={onNavigate}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Open Ada Lovelace' }));

    expect(onNavigate).toHaveBeenCalledWith('/users/user-1');
  });

  it('keeps the visible disable action inert unless a caller wires the flow', () => {
    render(
      <UsersTable
        users={users}
        capabilities={[
          decision('users.update', 'allowed'),
          decision('users.disable', 'allowed'),
          decision('users.create', 'allowed'),
        ]}
      />
    );

    const disableButton = screen.getByRole('button', { name: 'Disable Ada Lovelace' }) as HTMLButtonElement;
    expect(disableButton.disabled).toBe(true);
    expect(disableButton.title).toContain('Open user details');
  });

  it('calls the supplied disable handler when the table flow is wired', () => {
    const onDisable = vi.fn();
    render(
      <UsersTable
        users={users}
        capabilities={[
          decision('users.update', 'allowed'),
          decision('users.disable', 'allowed'),
          decision('users.create', 'allowed'),
        ]}
        onDisable={onDisable}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: 'Disable Ada Lovelace' }));

    expect(onDisable).toHaveBeenCalledWith(users[0]);
  });
});
