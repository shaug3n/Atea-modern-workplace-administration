import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OverviewPage } from '../../src/Web/src/features/overview/OverviewPage';

const fetchedAt = '2026-10-08T08:00:00Z';
const source = <T,>(state: string, data: T | null, scope: string, partialData = false) => ({ state, fetchedAt, data, scope, partialData });
const decision = (capability: string, state: string) => ({ capability, state, reasonCode: state });
const session = {
  user: { displayName: 'Workspace Operator' },
  workspace: { id: 'workspace-one', name: 'Customer', moduleAccess: ['users', 'licenses', 'devices'] },
  workspaceAccess: { role: 'workspace_admin', canManageMembers: true, canManageSettings: true },
};

function overview(overrides: Record<string, unknown> = {}) {
  return {
    effectiveModules: ['users', 'licenses', 'devices'],
    effectiveCapabilities: [
      decision('users.view', 'allowed'),
      decision('licenses.view', 'allowed'),
      decision('devices.view', 'allowed'),
      decision('audit.view', 'allowed'),
    ],
    users: source('fresh', { totalUsers: 120 }, 'tenant_wide_verified'),
    licenseCoverage: source('fresh', { assignedUsers: 90, totalUsers: 120, percentage: 75 }, 'tenant_wide_verified'),
    activity: source('fresh', { items: Array.from({ length: 6 }, (_, index) => ({
      action: `Safe action ${index + 1}`,
      outcome: index === 0 ? 'Failed' : 'Succeeded',
      timestamp: `2026-10-08T0${7 - index}:00:00Z`,
      targetId: 'must not leak',
      metadata: 'must not leak',
      correlationId: 'must not leak',
    })) }, 'workspace'),
    ...overrides,
  };
}

describe('overview and settings browser states', () => {
  afterEach(cleanup);

  it('renders_authorized_overview_and_safe_recent_activity', async () => {
    render(<OverviewPage loadOverview={async () => overview()} session={session} />);
    expect(await screen.findByText('workspace_admin')).toBeTruthy();
    expect(screen.getByText('Users access: Allowed')).toBeTruthy();
    expect(screen.getAllByText(/verified tenant-wide/i).length).toBeGreaterThan(0);
    expect(screen.getByText('assigned users of total users')).toBeTruthy();
    const activity = screen.getByRole('region', { name: 'Recent app activity' });
    expect(within(activity).getAllByRole('listitem')).toHaveLength(5);
    expect(within(activity).getByText('Safe action 1')).toBeTruthy();
    expect(within(activity).getByText('Failed')).toBeTruthy();
    expect(within(activity).queryByText('must not leak')).toBeNull();
    expect(within(activity).queryByText('correlationId')).toBeNull();
  });

  it('shows_PIM_guidance_before_partial_data_and_retries', async () => {
    const loadOverview = vi.fn()
      .mockResolvedValueOnce(overview({
        effectiveCapabilities: [
          decision('users.view', 'pim_activation_required'),
          decision('licenses.view', 'allowed'),
          decision('devices.view', 'allowed'),
          decision('audit.view', 'allowed'),
        ],
        users: source('restricted', null, 'unknown'),
        activity: source('stale', { items: [{ action: 'Safe activity', outcome: 'Succeeded', timestamp: fetchedAt }] }, 'workspace', true),
      }))
      .mockResolvedValueOnce(overview());
    render(<OverviewPage loadOverview={loadOverview} session={session} />);
    const pim = await screen.findByRole('link', { name: 'Open PIM guidance' });
    expect(pim.getAttribute('href')).toBe('/identity');
    expect(screen.getByText('Users access: PIM activation required')).toBeTruthy();
    expect(document.body.textContent).not.toContain('pim_activation_required');
    const actions = screen.getAllByRole('button', { name: 'Retry' });
    expect(document.querySelector('.overview-priority-actions')?.firstElementChild?.textContent).toContain('PIM');
    fireEvent.click(actions[0]);
    await waitFor(() => expect(loadOverview).toHaveBeenCalledTimes(2));
  });

  it('shows_restricted_empty_and_unavailable_audit_states', async () => {
    const initial = overview({ activity: source('empty', { items: [] }, 'workspace') });
    const { rerender } = render(<OverviewPage loadOverview={async () => initial} session={session} />);
    expect(await screen.findByText('No recent app activity.')).toBeTruthy();

    rerender(<OverviewPage loadOverview={async () => overview({
      effectiveCapabilities: [
        decision('users.view', 'allowed'),
        decision('licenses.view', 'allowed'),
        decision('devices.view', 'allowed'),
        decision('audit.view', 'hidden'),
      ],
      activity: source('restricted', null, 'workspace'),
    })} session={session} />);
    expect(await within(screen.getByRole('region', { name: 'Recent app activity' })).findByText(/app activity is restricted/i)).toBeTruthy();

    rerender(<OverviewPage loadOverview={async () => overview({ activity: source('unavailable', null, 'workspace') })} session={session} />);
    expect(await within(screen.getByRole('region', { name: 'Recent app activity' })).findByText(/app activity.*unavailable/i)).toBeTruthy();
    expect(screen.queryByText('No recent app activity.')).toBeNull();
  });

  it('keeps_supported_navigation_keyboard_reachable_at_narrow_width', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 360 });
    const navigate = vi.fn();
    render(<OverviewPage loadOverview={async () => overview()} session={session} onNavigate={navigate} />);
    const users = await screen.findByRole('link', { name: 'Users: 120, open Users page' });
    const licenses = screen.getByRole('link', { name: 'License coverage: 90 of 120, open Licenses page' });
    const licenseInfo = screen.getByRole('button', { name: 'About license coverage' });
    for (const control of [users, licenses, licenseInfo, screen.getByRole('link', { name: /Open devices/i }), screen.getByRole('link', { name: /Open activity/i })]) {
      expect(control.tabIndex).toBe(0);
      control.focus();
      expect(document.activeElement).toBe(control);
    }
    expect(users.getAttribute('aria-pressed')).toBeNull();
    expect(licenses.getAttribute('aria-pressed')).toBeNull();
    expect(screen.queryByRole('button', { name: /Users.*120/i })).toBeNull();
    licenseInfo.focus();
    fireEvent.keyDown(licenseInfo, { key: 'Enter' });
    expect(screen.getByRole('dialog', { name: 'About license coverage' })).toBeTruthy();
    fireEvent.click(users);
    expect(navigate).toHaveBeenCalledWith('/users');
    expect(window.innerWidth).toBe(360);
    expect(document.body.textContent).toContain('Recent app activity');
    expect(document.body.textContent?.toLowerCase()).toContain('verified tenant-wide');
  });
});
