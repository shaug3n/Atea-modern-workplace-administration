import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PrimaryNav } from '../../../src/Web/src/components/PrimaryNav';
import { App } from '../../../src/Web/src/app/App';
import { WorkspaceAccessPage } from '../../../src/Web/src/features/workspace-access/WorkspaceAccessPage';
import { WorkspaceModulesPage } from '../../../src/Web/src/features/workspace-settings/WorkspaceModulesPage';
import { ExchangeOverviewPage } from '../../../src/Web/src/features/exchange/ExchangeOverviewPage';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const session = {
  user: { displayName: 'Owner', userPrincipalName: 'owner@example.com' },
  workspaceAccess: {
    role: 'owner', isOwner: true, canManageMembers: true, canManageSettings: true, canManageModules: true,
  },
  workspace: { id: 'workspace-1', name: 'Contoso', enabledModules: ['users', 'devices', 'licenses'], moduleAccess: ['users', 'devices', 'licenses'] },
};

afterEach(() => { cleanup(); apiMock.mockReset(); window.history.replaceState({}, '', '/'); });

describe('customer workspace redesign', () => {
  it('renders grouped navigation from effective session modules and role access', () => {
    render(<PrimaryNav capabilities={null} session={session} currentPath="/overview" />);
    expect(screen.getByRole('link', { name: 'Overview' })).toBeTruthy();
    expect(screen.getByText('People')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Users' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Licenses' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Devices' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Exchange' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Workspace Settings' }).getAttribute('href')).toBe('/settings');
    expect(screen.queryByRole('link', { name: 'Modules' })).toBeNull();
  });

  it('shows Exchange only when the workspace and person both have access', () => {
    render(<PrimaryNav capabilities={null} session={{ ...session, workspace: { ...session.workspace, enabledModules: ['users', 'devices', 'licenses', 'exchange'], moduleAccess: ['users', 'devices', 'licenses', 'exchange'] }, workspaceAccess: { ...session.workspaceAccess } }} currentPath="/services/exchange" />);
    expect(screen.getByRole('link', { name: 'Exchange' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Exchange' }).getAttribute('aria-current')).toBe('page');
  });

  it.each([
    ['/onboarding', '/settings#connection'],
    ['/workspace-settings', '/settings#general'],
    ['/workspace-access', '/settings#access'],
  ])('redirects %s to %s', async (legacyPath, nextPath) => {
    window.history.replaceState({}, '', legacyPath);
    render(<App loadSession={async () => session} loadCapabilities={async () => ({ evaluatedAt: '2026-09-25T00:00:00Z', capabilities: [] })} />);
    await waitFor(() => expect(window.location.pathname + window.location.hash).toBe(nextPath));
  });

  it('allows Devices read-only and PIM-eligible states without treating them as hidden', async () => {
    window.history.replaceState({}, '', '/devices');
    render(<App loadSession={async () => session} loadCapabilities={async () => ({ evaluatedAt: '2026-09-25T00:00:00Z', capabilities: [{ capability: 'devices.view', state: 'read_only', reasonCode: 'role_read_only' }] })} />);
    expect(await screen.findByRole('heading', { name: 'Devices' })).toBeTruthy();
    expect(screen.queryByText('Permission required')).toBeNull();
  });

  it('shows device setup guidance to a workspace admin who has not been assigned the module', async () => {
    window.history.replaceState({}, '', '/devices');
    const adminSession = {
      ...session,
      workspaceAccess: { ...session.workspaceAccess, isOwner: false, canManageSettings: true, canManageModules: false },
      workspace: { ...session.workspace, moduleAccess: ['users', 'licenses'] },
    };
    render(<App loadSession={async () => adminSession} loadCapabilities={async () => ({ evaluatedAt: '2026-09-25T00:00:00Z', capabilities: [] })} />);
    expect(await screen.findByText('Device module access needed')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Devices' })).toBeTruthy();
  });

  it('gates a disabled module on a direct URL', async () => {
    window.history.replaceState({}, '', '/services/exchange');
    render(<App loadSession={async () => session} loadCapabilities={async () => ({ evaluatedAt: '2026-09-25T00:00:00Z', capabilities: [] })} />);
    expect(await screen.findByRole('heading', { name: 'Module disabled' })).toBeTruthy();
  });

  it('sends selected module keys when creating an invitation', async () => {
    apiMock.mockImplementation(async (path: string, init?: RequestInit) => {
      if (path === '/api/workspaces/current/access') return Response.json({ memberships: [], invitations: [] });
      if (path.endsWith('/invitations') && init?.method === 'POST') {
        expect(JSON.parse(String(init.body))).toMatchObject({ moduleKeys: ['users', 'devices'] });
        return Response.json({ invitationUrl: 'https://example.test/invite', expiresAt: '2026-10-01T00:00:00Z' });
      }
      return Response.json({ memberships: [], invitations: [] });
    });
    render(<WorkspaceAccessPage isOwner canManageModules availableModules={['users', 'devices', 'licenses']} />);
    await screen.findByRole('heading', { name: 'Workspace access' });
    fireEvent.change(screen.getByLabelText('Email address'), { target: { value: 'new@example.com' } });
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'New User' } });
    fireEvent.click(screen.getByLabelText('Users'));
    fireEvent.click(screen.getByLabelText('Devices'));
    fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Invitation created'));
  });

  it('keeps customer administrator grants owner-only while allowing a module administrator to edit modules', async () => {
    apiMock.mockResolvedValue(Response.json({ memberships: [], invitations: [] }));
    render(<WorkspaceAccessPage isOwner={false} canManageModules />);
    await screen.findByRole('heading', { name: 'Workspace access' });
    expect(screen.getByRole('option', { name: 'Member' })).toBeTruthy();
    expect(screen.queryByRole('option', { name: 'Customer administrator' })).toBeNull();
    expect((screen.getByLabelText('Exchange') as HTMLInputElement).disabled).toBe(false);
  });

  it('denies a member access to module assignment controls while retaining the access page state', async () => {
    apiMock.mockResolvedValue(Response.json({ memberships: [], invitations: [] }));
    render(<WorkspaceAccessPage isOwner={false} canManageModules={false} />);
    await screen.findByRole('heading', { name: 'Workspace access' });
    expect((screen.getByLabelText('Exchange') as HTMLInputElement).disabled).toBe(true);
  });

  it('lets the owner confirm transfer to an existing customer administrator', async () => {
    apiMock.mockImplementation(async (path: string, init?: RequestInit) => {
      if (path === '/api/workspaces/current/access') return Response.json({ memberships: [{ id: 'admin-1', tenantObjectId: 'tenant-admin-1', email: 'admin@example.com', platformRole: 'customer_admin', moduleKeys: ['users'], createdAt: '2026-09-25T00:00:00Z' }], invitations: [] });
      if (path === '/api/workspaces/current/access/ownership/transfer' && init?.method === 'POST') {
        expect(JSON.parse(String(init.body))).toEqual({ newOwnerMembershipId: 'admin-1' });
        return Response.json({ status: 'succeeded' });
      }
      return Response.json({ memberships: [], invitations: [] });
    });
    render(<WorkspaceAccessPage isOwner />);
    await screen.findByRole('heading', { name: 'Workspace access' });
    fireEvent.change(screen.getByLabelText('New owner'), { target: { value: 'admin-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Review ownership transfer' }));
    expect(screen.getByText(/transfer ownership to admin@example.com/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Confirm ownership transfer' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Ownership transfer requested'));
  });

  it('PATCHes workspace modules from the Modules settings page', async () => {
    apiMock.mockImplementation(async (path: string, init?: RequestInit) => {
      if (path === '/api/workspaces/current/modules') {
        if (init?.method === 'PATCH') {
          expect(JSON.parse(String(init.body))).toEqual({ enabledModules: ['users', 'devices', 'licenses', 'exchange'] });
          return Response.json({ enabledModules: ['users', 'devices', 'licenses', 'exchange'] });
        }
        return Response.json({ enabledModules: ['users', 'devices', 'licenses'] });
      }
      return new Response(null, { status: 404 });
    });
    render(<WorkspaceModulesPage />);
    await screen.findByRole('heading', { name: 'Modules' });
    fireEvent.click(screen.getByLabelText('Exchange'));
    fireEvent.click(screen.getByRole('button', { name: 'Save modules' }));
    expect((await screen.findByRole('status')).textContent).toContain('Modules saved');
  });

  it('labels Exchange directory records as unverified and shows row-level unavailable state', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path.startsWith('/api/exchange/mailboxes?')) return Response.json({ items: [{ userId: 'u1', displayName: 'Alex Morgan', address: 'alex@example.com' }], continuationToken: null });
      if (path.endsWith('/overview')) return new Response(null, { status: 503 });
      return new Response(null, { status: 404 });
    });
    render(<ExchangeOverviewPage />);
    expect(await screen.findByText('Alex Morgan')).toBeTruthy();
    expect(screen.getByText('Unverified')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Verify Exchange details for Alex Morgan' }));
    expect(await screen.findByText('Mailbox details are unavailable for this entry.')).toBeTruthy();
  });

  it('shows verified Exchange mailbox type and selected settings on demand', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path.startsWith('/api/exchange/mailboxes?')) return Response.json({ items: [{ userId: 'u1', displayName: 'Alex Morgan', address: 'alex@example.com' }], continuationToken: null, isCompleteExchangeInventory: false, limitation: 'Directory-backed only.' });
      if (path.endsWith('/overview')) return Response.json({ userId: 'u1', verificationStatus: 'verified', mailboxType: 'shared', settings: { timeZone: 'Europe/Oslo', language: 'en-US', automaticRepliesStatus: 'disabled' }, httpStatusCode: 200 });
      return new Response(null, { status: 404 });
    });
    render(<ExchangeOverviewPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Verify Exchange details for Alex Morgan' }));
    expect(await screen.findByText('Verified')).toBeTruthy();
    expect(screen.getByText('shared')).toBeTruthy();
    expect(screen.getByText('Europe/Oslo')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Open Exchange admin center' })).toBeTruthy();
  });
});
