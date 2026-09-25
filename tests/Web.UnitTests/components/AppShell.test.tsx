import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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
    expect(screen.getByRole('heading', { name: 'Workspace access is managed by an administrator' })).toBeTruthy();
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
    expect(screen.getByRole('link', { name: 'Workspace Settings' }).getAttribute('aria-current')).toBe('page');
    expect(screen.getByRole('navigation', { name: 'Breadcrumbs' }).textContent).toContain('General');
  });

  it('keeps unsupported Prism URLs out of routes and navigation', async () => {
    window.history.pushState(null, '', '/services/meeting-rooms');
    render(<App loadCapabilities={async () => allowedCapabilities} loadSession={async () => session} />);
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Page not found' })).toBeTruthy());
    const nav = screen.getByRole('navigation', { name: 'Primary navigation' });
    expect(nav.textContent).not.toContain('Meeting Rooms');
    expect(nav.textContent).not.toContain('Copilot');
  });

  it('blocks a direct Exchange URL when the workspace module is not assigned', async () => {
    window.history.pushState(null, '', '/services/exchange');
    render(<App loadCapabilities={async () => allowedCapabilities} loadSession={async () => session} />);
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Module disabled' })).toBeTruthy());
    expect(screen.queryByRole('heading', { name: 'Exchange' })).toBeNull();
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
  });

  it('keeps workspace settings available when the Graph capability check fails', async () => {
    window.history.pushState(null, '', '/settings');
    render(<App loadCapabilities={async () => { throw new Error('Graph unavailable'); }} loadSession={async () => ({ ...session, workspaceAccess: { role: 'workspace_owner', canManageSettings: true } })} />);
    expect(await screen.findByRole('heading', { name: 'Settings' })).toBeTruthy();
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
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 1 warnings/i })).toBeTruthy());
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('does not use an allowed decision from a nonauthoritative snapshot to load Users', async () => {
    window.history.pushState(null, '', '/users');
    render(<App loadCapabilities={async () => ({ ...allowedCapabilities, sourceState: 'temporarily_unavailable' })} loadSession={async () => session} />);
    expect(await screen.findByRole('heading', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown');
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
    let calls = 0;
    render(<App loadSession={async () => session} loadCapabilities={async () => {
      calls++;
      return calls === 1 ? { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'consent_required', reasonCode: 'consent_required' }] } : allowedCapabilities;
    }} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })} />);
    fireEvent.click(await screen.findByRole('button', { name: /notifications, 1 warnings/i }));
    expect(screen.getByText('Microsoft Graph consent required')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 0 warnings/i })).toBeTruthy());
  });

  it('rechecks capabilities when returning to the workspace tab', async () => {
    window.history.pushState(null, '', '/overview');
    let calls = 0;
    render(<App loadSession={async () => session} loadCapabilities={async () => {
      calls++;
      return calls === 1 ? { ...allowedCapabilities, capabilities: [{ capability: 'users.view', state: 'consent_required', reasonCode: 'consent_required' }] } : allowedCapabilities;
    }} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })} />);
    await screen.findByRole('button', { name: /notifications, 1 warnings/i });
    fireEvent.focus(window);
    await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 0 warnings/i })).toBeTruthy());
  });
});
