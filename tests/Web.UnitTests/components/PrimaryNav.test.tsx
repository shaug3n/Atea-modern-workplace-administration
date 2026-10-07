import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilitySnapshot, CapabilityState } from '../../../src/Web/src/capabilities/capabilityTypes';
import { PrimaryNav } from '../../../src/Web/src/components/PrimaryNav';

function snapshot(states: { users?: 'allowed' | 'hidden'; licenses?: 'allowed' | 'hidden'; audit?: CapabilityState; workspaceSettings?: 'allowed' | 'hidden' } = {}): CapabilitySnapshot {
  return {
    workspaceId: '55555555-5555-5555-5555-555555555555',
    evaluatedAt: '2026-09-20T10:00:00.000Z',
    capabilities: [
      { capability: 'users.view', state: states.users ?? 'allowed', reasonCode: 'active_role' },
      { capability: 'licenses.view', state: states.licenses ?? 'allowed', reasonCode: 'active_role' },
      { capability: 'audit.view', state: states.audit ?? 'allowed', reasonCode: 'workspace_platform_role' },
      { capability: 'workspace.settings.manage', state: states.workspaceSettings ?? 'allowed', reasonCode: 'workspace_platform_role' },
    ],
  };
}

describe('PrimaryNav', () => {
  afterEach(cleanup);

  it('hides Settings when the workspace session does not grant settings access', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/overview" session={{ user: {}, workspace: { id: 'w', name: 'Customer' }, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } }} />);

    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Workspace Settings' })).toBeNull();
  });

  it('retains an assigned Users module when Graph denies its view capability', () => {
    render(<PrimaryNav capabilities={snapshot({ users: 'hidden' })} currentPath="/overview" />);

    expect(screen.getByRole('link', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Licenses' })).toBeTruthy();
  });

  it('omits modules the person is not assigned', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/overview" session={{ user: {}, workspace: { id: 'w', name: 'Customer', enabledModules: ['users', 'licenses', 'exchange'], moduleAccess: ['users'] } }} />);

    expect(screen.queryByRole('link', { name: 'Licenses' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Users' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Services' })).toBeNull();
  });

  it('marks the active route for assistive technology', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/licenses" session={{ user: {}, workspace: { id: 'w', name: 'Customer' }, workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true, canManageModules: true } }} />);

    expect(screen.getByRole('link', { name: 'Licenses' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('link', { name: 'Workspace Settings' })).toBeTruthy();
  });

  it('exposes People and Services as keyboard-operable disclosures with reachable sublinks', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/overview" session={{ user: {}, workspace: { id: 'w', name: 'Customer', enabledModules: ['users', 'exchange'], moduleAccess: ['users', 'exchange'] } }} />);

    const people = screen.getByRole('button', { name: 'People' });
    const services = screen.getByRole('button', { name: 'Services' });
    expect(people.getAttribute('aria-expanded')).toBe('true');
    expect(services.getAttribute('aria-expanded')).toBe('true');
    fireEvent.click(people);
    expect(people.getAttribute('aria-expanded')).toBe('false');
    expect(screen.queryByRole('link', { name: 'Users' })).toBeNull();
    expect(people.tabIndex).toBe(0);
    fireEvent.click(people);
    expect(people.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('link', { name: 'Users' }).tabIndex).toBe(0);
  });

  it('keeps the active group open and marks only the most specific route active', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/services/exchange/details" session={{ user: {}, workspace: { id: 'w', name: 'Customer', enabledModules: ['exchange'], moduleAccess: ['exchange'] } }} />);

    const services = screen.getByRole('button', { name: 'Services' });
    expect(services.getAttribute('aria-expanded')).toBe('true');
    fireEvent.click(services);
    expect(services.getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('link', { name: 'Exchange' }).getAttribute('aria-current')).toBe('page');
    expect(screen.queryAllByRole('link', { current: 'page' })).toHaveLength(1);
  });

  it('offers one Workspace Settings destination for authorized members', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/settings/modules" session={{ user: {}, workspace: { id: 'w', name: 'Customer' }, workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true, canManageModules: true } }} />);

    expect(screen.getAllByRole('link', { name: 'Workspace Settings' })).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'Workspace Settings' }).getAttribute('aria-current')).toBe('page');
    expect(screen.queryByRole('link', { name: 'Modules' })).toBeNull();
  });

  it('hides the Activity link when audit.view is hidden', () => {
    render(<PrimaryNav capabilities={snapshot({ audit: 'hidden' })} currentPath="/overview" />);

    expect(screen.queryByRole('link', { name: 'Activity' })).toBeNull();
  });

  it.each(['consent_required', 'disabled'] as const)('keeps Activity visible when audit.view is %s', (audit) => {
    render(<PrimaryNav capabilities={snapshot({ audit })} currentPath="/activity" />);

    const activity = screen.getByRole('link', { name: 'Activity' });
    expect(activity.getAttribute('href')).toBe('/activity');
    expect(activity.getAttribute('aria-current')).toBe('page');
  });

  it('shows a decorative icon in every navigation link', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/users" session={{ user: {}, workspace: { id: 'w', name: 'Customer' }, workspaceAccess: { role: 'owner', canManageMembers: true, canManageSettings: true } }} />);
    const links = screen.getAllByRole('link');
    expect(links.length).toBeGreaterThan(3);
    for (const link of links) expect(link.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
  });
});
