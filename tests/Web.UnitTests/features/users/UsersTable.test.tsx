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

  const allCaps = [decision('users.update', 'allowed'), decision('users.disable', 'allowed'), decision('users.create', 'allowed')];

  it('gives compact readers the full identifier and a name link without a mutation control', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<UsersTable users={[{ ...users[0], userPrincipalName: 'a'.repeat(90) + '@example.com' }]} capabilities={[decision('users.update', 'hidden'), decision('users.disable', 'hidden')]} />);
    const compact = screen.getByRole('list', { name: 'Users' });
    expect(compact.textContent).toContain('a'.repeat(90) + '@example.com');
    expect(screen.getByRole('link', { name: 'Ada Lovelace' }).getAttribute('href')).toBe('/users/user-1');
    expect(screen.queryByRole('button', { name: /Actions for/ })).toBeNull();
    vi.unstubAllGlobals();
  });

  it('has no separate Open or More details buttons and shows type as a column', () => {
    render(<UsersTable users={users} capabilities={allCaps} />);
    expect(screen.queryByRole('button', { name: 'Open Ada Lovelace' })).toBeNull();
    expect(screen.queryByRole('button', { name: /More details for/ })).toBeNull();
    expect(screen.getByRole('columnheader', { name: 'User type' })).toBeTruthy();
    expect(screen.getByText('Member')).toBeTruthy();
  });

  it('hides the account status column when the related capability is hidden', () => {
    render(<UsersTable users={users} capabilities={[decision('users.update', 'hidden'), decision('users.disable', 'hidden'), decision('users.create', 'hidden')]} />);
    expect(screen.queryByRole('columnheader', { name: 'Account status' })).toBeNull();
    expect(screen.queryByText('Enabled')).toBeNull();
  });

  it('keeps the disable item visible but aria-disabled with the permission when read only', () => {
    render(<UsersTable users={users} capabilities={[decision('users.update', 'read_only'), decision('users.disable', 'read_only'), decision('users.create', 'hidden')]} />);
    expect(screen.getByRole('columnheader', { name: 'Account status' })).toBeTruthy();
    expect(screen.getByText('Enabled')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ada Lovelace' }));
    const item = screen.getByRole('menuitem', { name: 'Disable user' });
    expect(item.getAttribute('aria-disabled')).toBe('true');
    expect(item.textContent).toContain('Requires permission: Disable users');
  });

  it('navigates to the user detail route when the name link is used', () => {
    const onNavigate = vi.fn();
    render(<UsersTable users={users} capabilities={allCaps} onNavigate={onNavigate} />);
    fireEvent.click(screen.getByRole('link', { name: 'Ada Lovelace' }));
    expect(onNavigate).toHaveBeenCalledWith('/users/user-1');
  });

  it('lists Open details first and a danger Disable user after a separator', () => {
    const onNavigate = vi.fn();
    render(<UsersTable users={users} capabilities={allCaps} onNavigate={onNavigate} onDisable={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ada Lovelace' }));
    const items = screen.getAllByRole('menuitem');
    expect(items.map(item => item.getAttribute('aria-label'))).toEqual(['Open details', 'Disable user']);
    expect(items[1].className).toContain('button--danger');
    expect(document.querySelector('[role="separator"]')).toBeTruthy();
    fireEvent.click(items[0]);
    expect(onNavigate).toHaveBeenCalledWith('/users/user-1');
  });

  it('keeps the disable item inert unless a caller wires the flow', () => {
    render(<UsersTable users={users} capabilities={allCaps} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ada Lovelace' }));
    const item = screen.getByRole('menuitem', { name: 'Disable user' });
    expect(item.getAttribute('aria-disabled')).toBe('true');
    expect(item.textContent).toContain('Open user details');
  });

  it('calls the supplied disable handler when the table flow is wired', () => {
    const onDisable = vi.fn();
    render(<UsersTable users={users} capabilities={allCaps} onDisable={onDisable} />);
    fireEvent.click(screen.getByRole('button', { name: 'Actions for Ada Lovelace' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disable user' }));
    expect(onDisable).toHaveBeenCalledWith(users[0]);
  });

  it('keeps a 60-character UPN in the DOM with the full value as title and falls back to the UPN then Unnamed user', () => {
    const upn = 'a'.repeat(48) + '@example.com';
    render(<UsersTable users={[{ ...users[0], displayName: 'Ada', userPrincipalName: upn }, { ...users[0], id: 'u2', displayName: '', userPrincipalName: 'only@example.com', mail: '' }, { ...users[0], id: 'u3', displayName: '', userPrincipalName: '', mail: '' }]} capabilities={allCaps} />);
    expect(upn.length).toBe(60);
    expect(document.querySelector(`[title="${upn}"]`)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'only@example.com' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Unnamed user' })).toBeTruthy();
  });
});
