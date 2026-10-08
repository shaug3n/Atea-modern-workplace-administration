import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../../src/Web/src/app/App';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import { clearPendingFlow, writePendingFlow } from '../../../../src/Web/src/features/invitations/pendingFlow';

const api = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => api }));

const owner: AppSession = {
  user: { displayName: 'Owner' },
  workspace: { id: 'w-1', name: 'Contoso', enabledModules: ['users'], moduleAccess: ['users'] },
  workspaceAccess: { role: 'workspace_owner', isOwner: true, canManageMembers: true, canManageSettings: true, canManageModules: true, canManageMemberModules: true },
};

function renderAt(path: string, session: AppSession = owner) {
  window.history.replaceState(null, '', path);
  return render(<App loadSession={async () => session} loadCapabilities={async () => ({ workspaceId: 'w-1', evaluatedAt: '2026-09-25T00:00:00Z', sourceState: 'graph_authoritative', capabilities: [] })} />);
}

afterEach(() => { cleanup(); api.mockReset(); clearPendingFlow(); window.history.replaceState(null, '', '/'); });

describe('Workspace Settings hub', () => {
  it('shows one page heading and independently loaded sections for an owner', async () => {
    api.mockImplementation(async (path: string) => {
      if (path.endsWith('/modules')) return Response.json({ enabledModules: ['users'] });
      if (path.endsWith('/access')) return Response.json({ memberships: [], invitations: [] });
      if (path.endsWith('/settings')) return Response.json({ displayName: 'Contoso', enabledModules: ['users'], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } });
      if (path.endsWith('/connection-health')) return Response.json({ status: 'connected', lastVerifiedAt: null });
      return new Response(null, { status: 404 });
    });
    renderAt('/settings');
    expect(await screen.findByRole('heading', { level: 1, name: 'Workspace Settings' })).toBeTruthy();
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    for (const section of ['Connection', 'General', 'Modules', 'Access']) {
      expect(screen.getByRole('heading', { level: 2, name: section })).toBeTruthy();
    }
    await screen.findByText('No workspace members have been added yet.');
    expect(api).toHaveBeenCalledWith('/api/workspaces/current/access');
  });

  it('indexes sections, marks the hash section current, focuses it and shows one freshness line', async () => {
    api.mockImplementation(async (path: string) => {
      if (path.endsWith('/modules')) return Response.json({ enabledModules: ['users'] });
      if (path.endsWith('/access')) return Response.json({ memberships: [], invitations: [] });
      if (path.endsWith('/settings')) return Response.json({ displayName: 'Contoso', enabledModules: ['users'], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } });
      if (path.endsWith('/connection-health')) return Response.json({ status: 'connected', lastVerifiedAt: null });
      return new Response(null, { status: 404 });
    });
    renderAt('/settings#modules');
    const nav = await screen.findByRole('navigation', { name: 'Settings sections' });
    expect(Array.from(nav.querySelectorAll('a')).map(link => link.textContent)).toEqual(['Connection', 'General', 'Modules', 'Access']);
    expect(nav.querySelector('a[href="#modules"]')?.getAttribute('aria-current')).toBe('true');
    window.location.hash = '#access';
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { level: 2, name: 'Access' })));
    expect(nav.querySelector('a[href="#access"]')?.getAttribute('aria-current')).toBe('true');
    expect(nav.querySelector('a[href="#modules"]')?.getAttribute('aria-current')).toBeNull();
    expect(document.body.textContent).not.toContain('Retrieved');
  });

  it('keeps General and Access available when Modules fails', async () => {
    api.mockImplementation(async (path: string) => {
      if (path.endsWith('/modules')) return new Response(null, { status: 503 });
      if (path.endsWith('/access')) return Response.json({ memberships: [], invitations: [] });
      if (path.endsWith('/settings')) return Response.json({ displayName: 'Contoso', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } });
      return Response.json({ status: 'connected', lastVerifiedAt: null });
    });
    renderAt('/settings');
    expect(await screen.findByText('Workspace modules are unavailable.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeTruthy();
    expect(screen.getByText('No workspace members have been added yet.')).toBeTruthy();
  });

  it('shows Access to a member manager without requesting unauthorized sections', async () => {
    api.mockResolvedValue(Response.json({ memberships: [], invitations: [] }));
    renderAt('/settings#access', { ...owner, workspaceAccess: { role: 'customer_admin', canManageMembers: true, canManageSettings: false, canManageModules: false } });
    expect(await screen.findByRole('heading', { level: 1, name: 'Workspace Settings' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Workspace Settings' })).toBeTruthy();
    expect(screen.getByRole('heading', { level: 2, name: 'Access' })).toBeTruthy();
    expect(screen.queryByRole('heading', { level: 2, name: 'General' })).toBeNull();
    expect(screen.queryByRole('heading', { level: 2, name: 'Modules' })).toBeNull();
    await waitFor(() => expect(api.mock.calls.map(call => call[0])).toEqual(['/api/workspaces/current/access']));
  });

  it('denies a member without any settings permissions', async () => {
    renderAt('/settings', { ...owner, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false, canManageModules: false } });
    expect(await screen.findByText(/You don't have access to/)).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Connection' })).toBeNull();
    expect(api).not.toHaveBeenCalled();
  });

  it.each([
    ['/onboarding', 'connection'], ['/settings/setup', 'connection'],
    ['/workspace-settings', 'general'], ['/settings/general', 'general'],
    ['/settings/modules', 'modules'], ['/workspace-access', 'access'], ['/settings/access', 'access'],
    ['/settings/general/', 'general'], ['/settings/general/#old', 'general'], ['/workspace-access#old', 'access'], ['/onboarding/#old', 'connection'],
    ['/settings#access', 'access'],
  ])('focuses %s on the %s section after direct load', async (path, section) => {
    api.mockImplementation(async (url: string) => {
      if (url.endsWith('/access')) return Response.json({ memberships: [], invitations: [] });
      if (url.endsWith('/modules')) return Response.json({ enabledModules: [] });
      if (url.endsWith('/settings')) return Response.json({ displayName: 'Contoso', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } });
      return Response.json({ status: 'connected', lastVerifiedAt: null });
    });
    renderAt(path);
    await waitFor(() => expect(window.location.pathname + window.location.hash).toBe(`/settings#${section}`));
    const heading = await screen.findByRole('heading', { level: 2, name: section[0].toUpperCase() + section.slice(1) });
    await waitFor(() => expect(document.activeElement).toBe(heading));
  });

  it('validates persisted workspace callback state and clears query data with a trailing slash', async () => {
    api.mockImplementation(async (path: string) => {
      if (path.endsWith('/consent/complete')) return Response.json({ valid: true, status: 'consent_received' });
      if (path.endsWith('/connection-health/check')) return Response.json({ status: 'connected' });
      return new Response(null, { status: 404 });
    });
    writePendingFlow({
      kind: 'workspace',
      challenge: 'signed',
      tenantId: '11111111-1111-1111-1111-111111111111',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      step: 'workspace_callback',
    });
    renderAt('/onboarding/consent/callback/?state=signed&tenant=11111111-1111-1111-1111-111111111111#old');
    await waitFor(() => expect(api.mock.calls.some(([path]) => path === '/api/workspaces/current/consent/complete')).toBe(true));
    expect(window.location.pathname).toBe('/onboarding/consent/callback/');
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('');
  });
});
