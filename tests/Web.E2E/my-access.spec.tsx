import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import type { AppSession } from '../../src/Web/src/components/TenantContextHeader';
import type { Capability, CapabilitySnapshot } from '../../src/Web/src/capabilities/capabilityTypes';

const capabilities: Capability[] = [
  'users.view', 'users.create', 'users.update', 'users.disable', 'users.reset_password',
  'users.sessions.revoke', 'groups.manage_members', 'authentication.methods.view',
  'authentication.methods.manage', 'authentication.campaigns.view', 'pim.view', 'pim.activate', 'roles.assign',
  'devices.view', 'devices.manage', 'devices.privileged.manage', 'devices.bitlocker.metadata',
  'devices.bitlocker.reveal', 'devices.laps.metadata', 'devices.laps.reveal',
  'licenses.view', 'licenses.assign', 'audit.view', 'workspace.settings.manage',
  'workspace.members.manage',
];

const memberSession: AppSession = {
  user: { displayName: 'Casey Member', userPrincipalName: 'casey@example.com' },
  workspace: {
    id: 'workspace-1',
    name: 'Northwind Workspace',
    enabledModules: ['users', 'devices', 'licenses', 'authentication-campaigns'],
    moduleAccess: ['users', 'devices', 'licenses', 'authentication-campaigns'],
  },
  workspaceAccess: {
    role: 'member',
    canManageMembers: false,
    canManageSettings: false,
    canManageModules: false,
    canManageMemberModules: false,
  },
};

function accessSnapshot(workspaceId = memberSession.workspace.id): CapabilitySnapshot {
  return {
    workspaceId,
    evaluatedAt: '2026-10-08T16:00:00Z',
    sourceState: 'graph_authoritative',
    workspaceModules: ['users', 'devices', 'licenses', 'authentication-campaigns'].map(module => ({
      module,
      grantSource: 'workspace_role',
      enabled: true,
      effective: true,
    })),
    capabilities: capabilities.map(capability => ({
      capability,
      state: capability === 'users.view' ? 'allowed' : 'hidden',
      reasonCode: capability === 'users.view' ? 'active_role' : 'role_read_only',
      roleEvidence: { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] },
    })),
  };
}

function openAccountMenu(viewport: 'desktop' | 'mobile') {
  if (viewport === 'mobile') {
    fireEvent.click(screen.getByRole('button', { name: 'Menu' }));
    const menu = document.querySelector('.mobile-nav-extras .account-access-menu');
    expect(menu).not.toBeNull();
    fireEvent.click((menu as HTMLElement).querySelector('summary')!);
    return menu as HTMLElement;
  }

  const menu = document.querySelector('.app-header__account .account-access-menu');
  expect(menu).not.toBeNull();
  fireEvent.click((menu as HTMLElement).querySelector('summary')!);
  return menu as HTMLElement;
}

afterEach(() => {
  cleanup();
  window.history.replaceState({}, '', '/');
  vi.unstubAllGlobals();
});

describe('My access owned journey', () => {
  it.each(['desktop', 'mobile'] as const)(
    'lets an ordinary member navigate from the %s DOMAIN ACCESS disclosure',
    async viewport => {
      window.history.replaceState({}, '', '/identity');
      const loadCapabilities = vi.fn(async () => accessSnapshot());
      render(
        <App
          loadSession={async () => memberSession}
          loadCapabilities={loadCapabilities}
          loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })}
        />,
      );

      expect(await screen.findByText('Casey Member')).toBeTruthy();
      const menu = openAccountMenu(viewport);
      fireEvent.click(within(menu).getByRole('link', { name: 'My access' }));

      expect(await screen.findByRole('heading', { name: 'My access' })).toBeTruthy();
      expect(window.location.pathname).toBe('/my-access');
      expect(screen.getByText(/Review how workspace grants and Microsoft permissions affect actions in Northwind Workspace/)).toBeTruthy();
      expect(screen.getByRole('heading', { name: 'Users', level: 2 })).toBeTruthy();
      expect(screen.getByRole('heading', { name: 'Authentication campaigns', level: 2 })).toBeTruthy();
      expect(screen.getByText('View authentication campaigns')).toBeTruthy();
      expect(screen.getByText('View users')).toBeTruthy();
      expect(loadCapabilities).toHaveBeenCalledOnce();
    },
  );

  it('retries one shared snapshot for both the account summary and page', async () => {
    window.history.replaceState({}, '', '/my-access');
    const loadCapabilities = vi.fn()
      .mockRejectedValueOnce(new Error('temporary access API failure'))
      .mockResolvedValueOnce(accessSnapshot());
    render(
      <App
        loadSession={async () => memberSession}
        loadCapabilities={loadCapabilities}
        loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })}
      />,
    );

    expect(await screen.findByText('Access evidence is unavailable')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText('The API explicitly allowed this action.')).toBeTruthy();
    const menu = openAccountMenu('desktop');
    expect(within(menu).getByText('Read access: Mixed results')).toBeTruthy();
    expect(loadCapabilities).toHaveBeenCalledTimes(2);
  });

  it('does not show a prior workspace snapshot while the session switches workspaces', async () => {
    window.history.replaceState({}, '', '/my-access');
    const firstSession = async () => memberSession;
    const secondSession = async () => ({
      ...memberSession,
      workspace: {
        id: 'workspace-2',
        name: 'Fabrikam Workspace',
        enabledModules: ['users', 'devices', 'licenses'],
        moduleAccess: ['users', 'devices', 'licenses'],
      },
    });
    const loadCapabilities = vi.fn()
      .mockResolvedValueOnce(accessSnapshot('workspace-1'))
      .mockResolvedValueOnce(accessSnapshot('workspace-2'));
    const app = render(
      <App
        loadSession={firstSession}
        loadCapabilities={loadCapabilities}
        loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })}
      />,
    );
    expect(await screen.findByRole('heading', { name: 'My access' })).toBeTruthy();
    expect(screen.getByText('Snapshot workspace ID:')).toBeTruthy();
    expect(screen.getByText('workspace-1')).toBeTruthy();

    app.rerender(
      <App
        loadSession={secondSession}
        loadCapabilities={loadCapabilities}
        loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })}
      />,
    );

    await waitFor(() => expect(screen.queryByText('workspace-1')).toBeNull());
    expect(screen.getAllByText(/Fabrikam Workspace/).length).toBeGreaterThan(0);
    expect(screen.getByText(/A check from another workspace cannot be shown here/)).toBeTruthy();
    expect(loadCapabilities).toHaveBeenCalledOnce();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Snapshot workspace ID:')).toBeTruthy();
    expect(screen.getByText('workspace-2')).toBeTruthy();
    expect(screen.queryByText('workspace-1')).toBeNull();
    expect(loadCapabilities).toHaveBeenCalledTimes(2);
  });

  it('keeps Graph and Exchange evidence explicitly unavailable without extra fetches or writes', async () => {
    window.history.replaceState({}, '', '/my-access');
    const fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
    const unavailableSnapshot: CapabilitySnapshot = {
      workspaceId: memberSession.workspace.id,
      evaluatedAt: '2026-10-08T16:00:00Z',
      sourceState: 'unavailable',
      workspaceModules: [{ module: 'users', grantSource: 'workspace_role', enabled: true, effective: true }],
      capabilities: [],
    };
    const loadCapabilities = vi.fn(async () => unavailableSnapshot);
    render(
      <App
        loadSession={async () => memberSession}
        loadCapabilities={loadCapabilities}
        loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: null })}
      />,
    );

    expect(await screen.findByText('Microsoft authorization evidence is unavailable')).toBeTruthy();
    const exchange = screen.getByRole('article', { name: 'Exchange' });
    const [exchangeRead, exchangeWrite] = within(exchange).getAllByRole('group');
    expect(within(exchangeRead).getByText('Unavailable')).toBeTruthy();
    expect(within(exchangeWrite).getByText('Unavailable')).toBeTruthy();
    expect(within(exchangeRead).getByText('Action coverage is not available; no access summary can be made.')).toBeTruthy();

    const page = screen.getByRole('region', { name: 'My access evidence' });
    fireEvent.click(within(page).getByRole('button', { name: 'Refresh access' }));
    await waitFor(() => expect(loadCapabilities).toHaveBeenCalledTimes(2));
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
