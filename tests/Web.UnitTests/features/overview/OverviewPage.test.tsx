import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';
import { overviewMessages } from '../../../../src/Web/src/features/overview/messages';
import { messages } from '../../../../src/Web/src/messages/en';

const fetchedAt = '2026-10-08T08:00:00Z';

function source<T>(state: string, data: T | null, scope: string, partialData = false) {
  return { state, fetchedAt, partialData, data, scope };
}

function decision(capability: string, state: string, nextStep?: { label: string; href?: string }) {
  return { capability, state, reasonCode: state, ...(nextStep ? { nextStep } : {}) };
}

const completeOverview = {
  effectiveModules: ['users', 'licenses', 'devices'],
  effectiveCapabilities: [
    decision('users.view', 'allowed'),
    decision('licenses.view', 'allowed'),
    decision('devices.view', 'allowed'),
    decision('audit.view', 'allowed'),
  ],
  users: source('fresh', { totalUsers: 42 }, 'tenant_wide_verified'),
  licenseCoverage: source('fresh', { assignedUsers: 30, totalUsers: 42, percentage: 71 }, 'tenant_wide_verified'),
  activity: source('fresh', { items: [
    { action: 'User created', outcome: 'Succeeded', timestamp: fetchedAt },
    { action: 'Password reset', outcome: 'Failed', timestamp: fetchedAt },
  ] }, 'workspace'),
};

const session = {
  user: { displayName: 'Alex Admin' },
  workspace: { id: 'w', name: 'Customer', moduleAccess: ['users', 'licenses', 'devices'] },
  workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true },
};

describe('OverviewPage', () => {
  afterEach(cleanup);

  it('composes feature copy into the existing English messages', () => {
    expect(messages.overviewEyebrow).toBe(overviewMessages.overviewEyebrow);
    expect(messages.overviewTitle).toBe(overviewMessages.overviewTitle);
    expect(messages.overviewLoading).toBe(overviewMessages.overviewLoading);
    expect(messages.overviewUnavailable).toBe(overviewMessages.overviewUnavailable);
    expect(messages.overviewRole).toBe(overviewMessages.overviewRole);
  });

  it('shows_workspace_role_and_section_specific_scope', async () => {
    render(<OverviewPage loadOverview={async () => completeOverview} session={session} />);
    expect(await screen.findByText('workspace_owner')).toBeTruthy();
    expect(screen.getByText('Users access: allowed')).toBeTruthy();
    expect(screen.getByText('Licenses access: allowed')).toBeTruthy();
    expect(screen.getByText(/users.*verified tenant-wide/i)).toBeTruthy();
    expect(screen.getByText(/license coverage.*verified tenant-wide/i)).toBeTruthy();
    expect(screen.getByText(/app activity.*workspace/i)).toBeTruthy();
  });

  it('shows_only_verified_user_and_user_license_coverage', async () => {
    const navigate = vi.fn();
    render(<OverviewPage loadOverview={async () => completeOverview} session={session} onNavigate={navigate} />);
    const users = await screen.findByRole('button', { name: /Users.*42/i });
    const licenses = screen.getByRole('button', { name: /License coverage.*30 of 42/i });
    expect(users.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByText('assigned users of total users')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'About license coverage' }));
    expect(screen.getByRole('dialog', { name: 'About license coverage' }).textContent).toMatch(/not.*purchased-seat/i);
    fireEvent.click(users);
    fireEvent.click(licenses);
    expect(navigate.mock.calls).toEqual([['/users'], ['/licenses']]);
  });

  it('does_not_render_links_or_numbers_for_restricted_sections', async () => {
    const restricted = {
      ...completeOverview,
      effectiveCapabilities: [
        decision('users.view', 'read_only'),
        decision('licenses.view', 'consent_required'),
        decision('devices.view', 'hidden'),
        decision('audit.view', 'hidden'),
      ],
      users: source('restricted', { totalUsers: 42 }, 'scoped'),
      licenseCoverage: source('fresh', { assignedUsers: 30, totalUsers: 42, percentage: 71 }, 'unverified'),
      activity: source('restricted', null, 'workspace'),
    };
    render(<OverviewPage loadOverview={async () => restricted} session={session} />);
    await screen.findByText('Users access: read_only');
    expect(screen.queryByRole('button', { name: /Users.*42/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /License coverage/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /Open devices/i })).toBeNull();
    expect(screen.queryByText('42')).toBeNull();
    expect(screen.queryByText('30')).toBeNull();
    expect(within(screen.getByRole('region', { name: 'Recent app activity' })).getByText(/app activity is restricted/i)).toBeTruthy();
  });

  it('keeps_empty_restricted_unavailable_stale_and_partial_states_distinct', async () => {
    const mixed = {
      ...completeOverview,
      users: source('stale', { totalUsers: 42 }, 'tenant_wide_verified', true),
      licenseCoverage: source('unavailable', null, 'tenant_wide_verified'),
      activity: source('empty', { items: [] }, 'workspace'),
    };
    render(<OverviewPage loadOverview={async () => mixed} session={session} />);
    expect(await screen.findByRole('button', { name: /Users.*42/i })).toBeTruthy();
    expect(screen.getAllByText(/may be out of date/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/partly loaded/i)).toBeTruthy();
    expect(screen.getByText('No recent app activity.')).toBeTruthy();
    expect(screen.getByText(/license coverage.*unavailable/i)).toBeTruthy();

    cleanup();
    const denied = { ...completeOverview, activity: source('restricted', null, 'workspace') };
    render(<OverviewPage loadOverview={async () => denied} session={session} />);
    const activityRegion = await screen.findByRole('region', { name: 'Recent app activity' });
    expect(await within(activityRegion).findByText(/app activity is restricted/i)).toBeTruthy();
    expect(screen.queryByText('No recent app activity.')).toBeNull();
  });

  it('ranks_access_then_freshness_then_supported_navigation_stably', async () => {
    const overview = {
      ...completeOverview,
      effectiveCapabilities: [
        decision('users.view', 'pim_activation_required', { label: 'Unsafe API suggestion', href: '/api/overview' }),
        decision('licenses.view', 'consent_required'),
        decision('devices.view', 'allowed'),
        decision('audit.view', 'allowed'),
      ],
      users: source('restricted', null, 'unknown'),
      licenseCoverage: source('unavailable', null, 'tenant_wide_verified'),
      activity: source('stale', { items: [{ action: 'Recent action', outcome: 'Succeeded', timestamp: fetchedAt }] }, 'workspace', true),
    };
    render(<OverviewPage loadOverview={async () => overview} session={session} />);
    await screen.findByRole('link', { name: 'Open PIM guidance' });
    const actions = Array.from(document.querySelectorAll('.overview-priority-actions a, .overview-priority-actions button'));
    expect(actions.map(item => item.textContent?.trim())).toEqual(['Open PIM guidance', 'Open setup', 'Retry', 'Open devices', 'Open activity']);
    expect(screen.queryByRole('link', { name: /Unsafe API/i })).toBeNull();
    expect(screen.getByRole('link', { name: 'Open PIM guidance' }).getAttribute('href')).toBe('/identity');
    expect(screen.getByRole('link', { name: 'Open setup' }).getAttribute('href')).toBe('/settings#connection');
    expect(screen.getByRole('link', { name: 'Open devices' }).getAttribute('href')).toBe('/devices');
    expect(screen.getByRole('link', { name: 'Open activity' }).getAttribute('href')).toBe('/activity');
    expect(Array.from(document.querySelectorAll('.overview-priority-actions a')).map(link => link.getAttribute('href'))).toEqual([
      '/identity', '/settings#connection', '/devices', '/activity',
    ]);
  });

  it('uses_only_supported_routes_for_actions', async () => {
    const restrictedSettings = {
      ...completeOverview,
      effectiveCapabilities: [
        decision('users.view', 'consent_required', { label: 'Unsafe', href: 'https://example.invalid' }),
        decision('licenses.view', 'allowed'),
        decision('devices.view', 'hidden'),
        decision('audit.view', 'hidden'),
      ],
      users: source('unavailable', null, 'tenant_wide_verified'),
      activity: source('restricted', null, 'workspace'),
    };
    const limitedSession = { ...session, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } };
    render(<OverviewPage loadOverview={async () => restrictedSettings} session={limitedSession} />);
    expect(await screen.findByText('Licenses access: allowed')).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Open setup' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Unsafe' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Open devices' })).toBeNull();
    expect(screen.getAllByRole('button', { name: /License coverage/i }).length).toBeGreaterThan(0);
  });

  it('unknown_source_state_fails_closed', async () => {
    const unknown = {
      ...completeOverview,
      users: source('future_state', { totalUsers: 42 }, 'tenant_wide_verified'),
      licenseCoverage: source('fresh', { assignedUsers: 42, totalUsers: 30, percentage: 140 }, 'tenant_wide_verified'),
      activity: source('fresh', { items: [{ action: 'Unexpected scope row', outcome: 'Succeeded', timestamp: fetchedAt }] }, 'unknown'),
    };
    render(<OverviewPage loadOverview={async () => unknown} session={session} />);
    await screen.findByText(/users.*unavailable/i);
    expect(screen.queryByRole('button', { name: /Users.*42/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /Users/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /License coverage 42 of 30/i })).toBeNull();
    expect(screen.getByText('Retry Licenses')).toBeTruthy();
    expect(screen.queryByText('Unexpected scope row')).toBeNull();
  });

  it('retries by issuing one new overview load', async () => {
    const loadOverview = vi.fn()
      .mockResolvedValueOnce({ ...completeOverview, users: source('stale', { totalUsers: 42 }, 'tenant_wide_verified') })
      .mockResolvedValueOnce(completeOverview);
    render(<OverviewPage loadOverview={loadOverview} session={session} />);
    await screen.findByRole('button', { name: /Users.*42/i });
    fireEvent.click(screen.getAllByRole('button', { name: 'Retry' })[0]);
    await waitFor(() => expect(loadOverview).toHaveBeenCalledTimes(2));
  });
});
