import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import { PrimaryNav } from '../../../src/Web/src/components/PrimaryNav';

function snapshot(workspaceSettingsState: 'allowed' | 'hidden'): CapabilitySnapshot {
  return {
    workspaceId: '55555555-5555-5555-5555-555555555555',
    evaluatedAt: '2026-09-20T10:00:00.000Z',
    capabilities: [
      { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' },
      { capability: 'workspace.settings.manage', state: workspaceSettingsState, reasonCode: 'workspace_platform_role' },
    ],
  };
}

describe('PrimaryNav', () => {
  afterEach(cleanup);

  it('hides Workspace settings when the existing workspace settings capability is unavailable', () => {
    render(<PrimaryNav capabilities={snapshot('hidden')} currentPath="/overview" />);

    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Workspace settings' })).toBeNull();
  });

  it('marks the active route for assistive technology', () => {
    render(<PrimaryNav capabilities={snapshot('allowed')} currentPath="/licenses" />);

    expect(screen.getByRole('link', { name: 'Licenses' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('link', { name: 'Workspace settings' })).toBeTruthy();
  });
});
