import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import { PrimaryNav } from '../../../src/Web/src/components/PrimaryNav';

function snapshot(states: { users?: 'allowed' | 'hidden'; licenses?: 'allowed' | 'hidden'; audit?: 'allowed' | 'hidden'; workspaceSettings?: 'allowed' | 'hidden' } = {}): CapabilitySnapshot {
  return {
    workspaceId: '55555555-5555-5555-5555-555555555555',
    evaluatedAt: '2026-09-20T10:00:00.000Z',
    capabilities: [
      { capability: 'users.view', state: states.users ?? 'allowed', reasonCode: 'active_role' },
      { capability: 'licenses.assign', state: states.licenses ?? 'allowed', reasonCode: 'active_role' },
      { capability: 'audit.view', state: states.audit ?? 'allowed', reasonCode: 'workspace_platform_role' },
      { capability: 'workspace.settings.manage', state: states.workspaceSettings ?? 'allowed', reasonCode: 'workspace_platform_role' },
    ],
  };
}

describe('PrimaryNav', () => {
  afterEach(cleanup);

  it('hides Workspace settings when the existing workspace settings capability is unavailable', () => {
    render(<PrimaryNav capabilities={snapshot({ workspaceSettings: 'hidden' })} currentPath="/overview" />);

    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Workspace settings' })).toBeNull();
  });

  it('hides Users when users.view is unavailable', () => {
    render(<PrimaryNav capabilities={snapshot({ users: 'hidden' })} currentPath="/overview" />);

    expect(screen.queryByRole('link', { name: 'Users' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Licenses' })).toBeTruthy();
  });

  it('hides Licenses when licenses.assign is unavailable', () => {
    render(<PrimaryNav capabilities={snapshot({ licenses: 'hidden' })} currentPath="/overview" />);

    expect(screen.queryByRole('link', { name: 'Licenses' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Users' })).toBeTruthy();
  });

  it('marks the active route for assistive technology', () => {
    render(<PrimaryNav capabilities={snapshot()} currentPath="/licenses" />);

    expect(screen.getByRole('link', { name: 'Licenses' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('link', { name: 'Workspace settings' })).toBeTruthy();
  });

  it('hides Audit when audit.view is unavailable', () => {
    render(<PrimaryNav capabilities={snapshot({ audit: 'hidden' })} currentPath="/overview" />);

    expect(screen.queryByRole('link', { name: 'Audit activity' })).toBeNull();
  });
});
