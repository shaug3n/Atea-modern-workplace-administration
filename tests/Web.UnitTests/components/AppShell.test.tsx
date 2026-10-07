import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import { App } from '../../../src/Web/src/app/App';
import { AppShell } from '../../../src/Web/src/components/AppShell';
import { ThemeProvider } from '../../../src/Web/src/components/ThemeToggle';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const allowedCapabilities: CapabilitySnapshot = {
  workspaceId: '55555555-5555-5555-5555-555555555555',
  evaluatedAt: '2026-09-20T10:00:00.000Z',
  sourceState: 'graph_authoritative',
  capabilities: [
    { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'workspace.settings.manage', state: 'allowed', reasonCode: 'workspace_platform_role' },
  ],
};

const hiddenWorkspaceSettings: CapabilitySnapshot = {
  ...allowedCapabilities,
  capabilities: allowedCapabilities.capabilities.map((decision) =>
    decision.capability === 'workspace.settings.manage'
      ? { ...decision, state: 'hidden' as const, reasonCode: 'workspace_platform_role_required' }
      : decision),
};

const session = {
  user: { displayName: 'Alex Morgan', userPrincipalName: 'alex@example.com' },
  workspace: { id: '55555555-5555-5555-5555-555555555555', name: 'Contoso Workplace', enabledModules: ['users', 'devices', 'licenses'], moduleAccess: ['users', 'devices', 'licenses'] },
};

describe('AppShell', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
    window.history.pushState(null, '', '/');
    document.documentElement.removeAttribute('data-theme');
  });

  it('renders the Atea logo once as the home link in the application shell', async () => {
    render(
      <ThemeProvider systemTheme={() => 'light'}>
        <AppShell capabilities={allowedCapabilities} currentPath="/overview" session={session}>
          <p>Overview content</p>
        </AppShell>
      </ThemeProvider>
    );

    const logoLink = screen.getByRole('link', { name: 'Atea Unified Workplace home' });
    expect(logoLink.getAttribute('href')).toBe('/overview');
    expect(logoLink.querySelectorAll('img[data-logo-asset="atea-logo-grey.svg"]')).toHaveLength(1);
    expect(screen.getByText('Contoso Workplace')).toBeTruthy();
    expect(screen.getByText('Alex Morgan')).toBeTruthy();
  });

  it('keeps direct Workspace settings routes reachable as a permission state when navigation is hidden', async () => {
    window.history.pushState(null, '', '/workspace-settings');
    render(<App loadCapabilities={async () => hiddenWorkspaceSettings} loadSession={async () => session} />);

    await waitFor(() => expect(window.location.pathname).toBe('/settings'));
    expect(screen.getByText(/You don't have access to/)).toBeTruthy();
  });

  it('closes mobile navigation on Escape and shows the current settings breadcrumb', () => {
    render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/settings/general" session={{ ...session, workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true } }}><h1>General settings</h1></AppShell></ThemeProvider>);
    fireEvent.click(screen.getByRole('button', { name: 'Menu' }));
    expect(screen.getByRole('button', { name: 'Close menu' }).getAttribute('aria-expanded')).toBe('true');
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.getByRole('button', { name: 'Menu' }).getAttribute('aria-expanded')).toBe('false');
    const breadcrumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(breadcrumbs.textContent).toContain('Settings');
    expect(breadcrumbs.textContent).toContain('General');
  });

  it('does not link the Services breadcrumb to an unregistered route', () => {
    render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/services/exchange" session={session}><h1>Exchange</h1></AppShell></ThemeProvider>);
    const breadcrumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(breadcrumbs.textContent).toContain('Services');
    expect(breadcrumbs.querySelector('a[href="/services"]')).toBeNull();
  });

  it('shows one Workspace Settings navigation link while retaining the current section breadcrumb', () => {
    render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/settings/general" session={{ ...session, workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true } }}><h1>General settings</h1></AppShell></ThemeProvider>);

    const nav = screen.getByRole('navigation', { name: 'Primary navigation' });
    expect(nav.querySelectorAll('a[href="/settings"]')).toHaveLength(1);
    expect(within(nav).getByRole('link', { name: 'Workspace Settings' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('navigation', { name: 'Breadcrumbs' }).textContent).toContain('General');
  });

  it('keeps unsupported Prism URLs out of routes and navigation', async () => {
    window.history.pushState(null, '', '/services/meeting-rooms');
    render(<App loadCapabilities={async () => allowedCapabilities} loadSession={async () => session} />);
    await waitFor(() => expect(screen.getByRole('heading', { name: "This page isn't available" })).toBeTruthy());
    const nav = screen.getByRole('navigation', { name: 'Primary navigation' });
    expect(nav.textContent).not.toContain('Meeting Rooms');
    expect(nav.textContent).not.toContain('Copilot');
  });

  it('blocks a direct Exchange URL when the workspace module is not assigned', async () => {
    window.history.pushState(null, '', '/services/exchange');
    render(<App loadCapabilities={async () => allowedCapabilities} loadSession={async () => session} />);
    await waitFor(() => expect(screen.getByText(/is turned off for this workspace/)).toBeTruthy());
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.queryByRole('link', { name: 'Exchange' })).toBeNull();
  });

  it('renders a read-only Users view and requests its data', async () => {
    window.history.pushState(null, '', '/users');
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [], continuationToken: null, fetchedAt: '2026-09-25T10:00:00Z', freshness: 'fresh', partialData: false }), { status: 200 }));
    const snapshot: CapabilitySnapshot = { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'read_only', reasonCode: 'role_read_only' }] };
    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    await waitFor(() => expect(apiMock).toHaveBeenCalled());
  });

  it('keeps the Users page heading and an unavailable data region without requesting protected data when consent is required', async () => {
    window.history.pushState(null, '', '/users');
    const snapshot: CapabilitySnapshot = { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'consent_required', reasonCode: 'delegated_scope_required', missingScopes: ['User.Read.All'] }] };
    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Search users')).toBeTruthy();
  });

  it('retains a Graph warning after navigating away from a denied Devices page', async () => {
    window.history.pushState(null, '', '/devices');
    const snapshot: CapabilitySnapshot = { ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state: 'hidden', reasonCode: 'role_required' }] };
    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);
    await screen.findByRole('heading', { name: 'Devices' });
    expect(screen.getByLabelText('Device filters')).toBeTruthy();
    const warning = await screen.findByRole('button', { name: 'Notifications, 1 need attention' });
    fireEvent.click(screen.getByRole('link', { name: 'Overview' }));
    await screen.findByRole('heading', { name: 'Overview' });
    fireEvent.click(screen.getByRole('button', { name: /Notifications, \d+ need attention/i }));
    expect(screen.getByText('Access needs attention')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalledWith('/api/devices');
  });

  it('keeps Activity filters and the unavailable region without requesting audit records when access is unknown', async () => {
    window.history.pushState(null, '', '/activity');
    render(<App loadCapabilities={async () => { throw new Error('Graph unavailable'); }} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Platform activity' })).toBeTruthy();
    expect(screen.getByRole('textbox', { name: 'Person' })).toBeTruthy();
    expect(screen.getByText(/Data cannot be shown right now/)).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalledWith(expect.stringContaining('/api/audit/events'));
  });

  it('clears a reported Graph route warning only after authoritative recovery', async () => {
    window.history.pushState(null, '', '/devices');
    const denied: CapabilitySnapshot = { ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state: 'hidden', reasonCode: 'role_required' }] };
    const recovered: CapabilitySnapshot = { ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }] };
    let checks = 0;
    apiMock.mockResolvedValue(Response.json({ items: [], total: 0, fetchedAt: '2026-09-25T10:00:00Z', freshness: 'live', partialData: false }));
    render(<App loadCapabilities={async () => ++checks === 1 ? denied : recovered} loadSession={async () => session} />);
    await screen.findByRole('button', { name: 'Notifications, 1 need attention' });
    fireEvent.focus(window);
    await waitFor(() => expect(checks).toBe(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Notifications, 0 need attention' })).toBeTruthy());
  });

  it('keeps workspace settings available when the Graph capability check fails', async () => {
    window.history.pushState(null, '', '/settings');
    render(<App loadCapabilities={async () => { throw new Error('Graph unavailable'); }} loadSession={async () => ({ ...session, workspaceAccess: { role: 'workspace_owner', canManageSettings: true } })} />);
    expect(await screen.findByRole('heading', { level: 1, name: 'Workspace Settings' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'General' })).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeTruthy();
  });

  it('keeps the Users frame during unavailable authorization without requesting data', async () => {
    window.history.pushState(null, '', '/users');
    render(<App loadCapabilities={async () => { throw new Error('Graph unavailable'); }} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('reports a missing view decision safely while keeping protected data unloaded', async () => {
    window.history.pushState(null, '', '/users');
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, capabilities: [] })} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 1 need attention/i })).toBeTruthy());
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('does not use an allowed decision from a nonauthoritative snapshot to load Users', async () => {
    window.history.pushState(null, '', '/users');
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, sourceState: 'temporarily_unavailable' })} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('does not load Users when an allowed decision lacks the authoritative source marker', async () => {
    window.history.pushState(null, '', '/users');
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, sourceState: undefined })} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it.each(['/devices', '/devices/device-1'])('holds %s while Graph authorization is loading', async (path) => {
    window.history.pushState(null, '', path);
    render(<App loadCapabilities={() => new Promise<CapabilitySnapshot>(() => {})} loadSession={async () => session} />);
    expect(await screen.findByText('Checking access…')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it.each(['/devices', '/devices/device-1'])('keeps %s unavailable without protected requests when the capability check fails', async (path) => {
    window.history.pushState(null, '', path);
    render(<App loadCapabilities={async () => { throw new Error('Graph unavailable'); }} loadSession={async () => session} />);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('Data cannot be shown'));
    expect(apiMock).not.toHaveBeenCalled();
  });

  it.each(['/devices', '/devices/device-1'])('keeps %s unavailable without protected requests for an unknown view decision', async (path) => {
    window.history.pushState(null, '', path);
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, capabilities: [] })} loadSession={async () => session} />);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('Data cannot be shown'));
    expect(apiMock).not.toHaveBeenCalled();
  });

  it.each(['allowed', 'read_only'] as const)('loads the Devices list with an authoritative %s view decision', async (state) => {
    window.history.pushState(null, '', '/devices');
    apiMock.mockResolvedValue(Response.json({ items: [], total: 0, fetchedAt: '2026-09-25T10:00:00Z', freshness: 'live', partialData: false, access: { state } }));
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state, reasonCode: 'active_role' }] })} loadSession={async () => session} />);
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/devices'));
  });

  it('loads direct device details with an authoritative view decision', async () => {
    window.history.pushState(null, '', '/devices/device-1');
    apiMock.mockResolvedValue(Response.json({ id: 'device-1', deviceName: 'Device One' }));
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state: 'read_only', reasonCode: 'role_read_only' }] })} loadSession={async () => session} />);
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1', expect.objectContaining({ cache: 'no-store' })));
  });

  it.each(['/devices', '/devices/device-1'])('shows setup guidance at %s for a settings admin without a Devices module grant', async (path) => {
    window.history.pushState(null, '', path);
    const setupSession = { ...session, workspace: { ...session.workspace, moduleAccess: ['users', 'licenses'] }, workspaceAccess: { role: 'workspace_admin', canManageSettings: true } };
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, capabilities: [{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }] })} loadSession={async () => setupSession} />);
    expect(await screen.findByText('Device module access needed')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('holds protected reads while a capability refresh is unresolved', async () => {
    window.history.pushState(null, '', '/users');
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [], continuationToken: null, fetchedAt: '2026-09-25T10:00:00Z', freshness: 'fresh', partialData: false }), { status: 200 }));
    let resolveRefresh: ((snapshot: CapabilitySnapshot) => void) | undefined;
    let calls = 0;
    render(<App loadCapabilities={() => ++calls === 1 ? Promise.resolve(allowedCapabilities) : new Promise(resolve => { resolveRefresh = resolve; })} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    await waitFor(() => expect(apiMock).toHaveBeenCalled());
    apiMock.mockClear();
    fireEvent.focus(window);
    await waitFor(() => expect(calls).toBe(2));
    expect(screen.getByText('Checking access…')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
    resolveRefresh?.(allowedCapabilities);
  });

  it('refreshes injected capabilities from the notifications menu', async () => {
    window.history.pushState(null, '', '/overview');
    apiMock.mockResolvedValue(Response.json({ freshness: 'live', fetchedAt: '2026-09-25T08:00:00Z', totalUsers: 1, licenseCoverage: { assigned: 0, available: 0, percentage: 0 }, permissionHealth: { state: 'healthy', allowedCount: 1, totalCount: 1 }, pimAttention: { requiresAttention: false, count: 0 }, partialData: false, access: { state: 'allowed' } }));
    let calls = 0;
    render(<App loadSession={async () => session} loadCapabilities={async () => {
      calls++;
      return calls === 1 ? { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'consent_required', reasonCode: 'consent_required' }] } : allowedCapabilities;
    }} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })} />);
    fireEvent.click(await screen.findByRole('button', { name: /notifications, 1 need attention/i }));
    expect(screen.getByText('Microsoft Graph consent required')).toBeTruthy();
    fireEvent.click(within(screen.getByRole('region', { name: 'Workspace notifications' })).getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 0 need attention/i })).toBeTruthy());
  });

  it('rechecks capabilities when returning to the workspace tab', async () => {
    window.history.pushState(null, '', '/overview');
    apiMock.mockResolvedValue(Response.json({ freshness: 'live', fetchedAt: '2026-09-25T08:00:00Z', totalUsers: 1, licenseCoverage: { assigned: 0, available: 0, percentage: 0 }, permissionHealth: { state: 'healthy', allowedCount: 1, totalCount: 1 }, pimAttention: { requiresAttention: false, count: 0 }, partialData: false, access: { state: 'allowed' } }));
    let calls = 0;
    render(<App loadSession={async () => session} loadCapabilities={async () => {
      calls++;
      return calls === 1 ? { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'consent_required', reasonCode: 'consent_required' }] } : allowedCapabilities;
    }} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })} />);
    await screen.findByRole('button', { name: /notifications, 1 need attention/i });
    fireEvent.focus(window);
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 0 need attention/i })).toBeTruthy());
  });

  it('shows nothing about access in the header when the snapshot is healthy and a chip when it is degraded', () => {
    const { rerender } = render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/overview" session={session}><p>x</p></AppShell></ThemeProvider>);
    expect(screen.queryByText(/Access snapshot|Entra roles and Graph/)).toBeNull();
    expect(screen.queryByRole('button', { name: /Access limited/ })).toBeNull();
    rerender(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={{ ...allowedCapabilities, sourceState: 'unavailable' }} currentPath="/overview" session={session}><p>x</p></AppShell></ThemeProvider>);
    fireEvent.click(screen.getByRole('button', { name: /Access limited/ }));
    expect(screen.getByRole('region', { name: 'Workspace notifications' })).toBeTruthy();
    expect(screen.getByText(/Access check: limited · checked/)).toBeTruthy();
  });

  it('shows initials and the display name in the account area and falls back for a missing name', () => {
    const { rerender, container } = render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/overview" session={{ ...session, user: { displayName: 'Sondre Haugen' } }}><p>x</p></AppShell></ThemeProvider>);
    expect(container.querySelector('.account-summary__avatar')?.textContent).toBe('SH');
    rerender(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/overview" session={{ ...session, user: { displayName: null, userPrincipalName: 'zed@example.com' } }}><p>x</p></AppShell></ThemeProvider>);
    expect(container.querySelector('.account-summary__avatar')?.textContent).toBe('Z');
  });

  it('uses generic Details crumbs for ids, links registered parents and labels main by the page title', () => {
    const { container } = render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/devices/3f2b8c1e-aaaa-bbbb-cccc-123456789abc" session={session}><h1 id="page-title">Laptop</h1></AppShell></ThemeProvider>);
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' });
    expect(crumbs.textContent).toContain('Details');
    expect(document.body.textContent).not.toContain('3f2b8c1e');
    expect(crumbs.querySelector('a')?.getAttribute('href')).toBe('/devices');
    expect(container.querySelector('main')?.getAttribute('aria-labelledby')).toBe('page-title');
  });

  it('puts the dark mode row and account in the mobile drawer, leaving one switch at a time, and closes on Escape', () => {
    render(<ThemeProvider systemTheme={() => 'light'}><AppShell capabilities={allowedCapabilities} currentPath="/overview" session={session}><p>x</p></AppShell></ThemeProvider>);
    expect(screen.getAllByRole('switch', { name: 'Dark mode' })).toHaveLength(1);
    const menu = screen.getByRole('button', { name: 'Menu' });
    fireEvent.click(menu);
    expect(screen.getAllByRole('switch', { name: 'Dark mode' })).toHaveLength(1);
    const drawer = document.getElementById('primary-navigation')!;
    expect(drawer.querySelector('.theme-toggle--row')).not.toBeNull();
    expect(drawer.textContent).toContain('Alex Morgan');
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(menu.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menu);
  });
});
