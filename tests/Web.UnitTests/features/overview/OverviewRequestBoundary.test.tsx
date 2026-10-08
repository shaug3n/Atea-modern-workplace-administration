import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';

const getApiToken = vi.hoisted(() => vi.fn(async () => 'test-token'));
vi.mock('../../../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => ({ getApiToken }) }));

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks(); });

it('loads recent app activity through the only Overview-page source request', async () => {
  const requested: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    requested.push(path);
    if (path === '/api/overview') return Response.json({
      effectiveModules: ['users', 'licenses', 'devices'],
      effectiveCapabilities: [
        { capability: 'users.view', state: 'allowed' },
        { capability: 'licenses.view', state: 'allowed' },
        { capability: 'devices.view', state: 'allowed' },
        { capability: 'audit.view', state: 'allowed' },
      ],
      users: { state: 'fresh', fetchedAt: '2026-10-08T08:00:00Z', partialData: false, data: { totalUsers: 42 }, scope: 'tenant_wide_verified' },
      licenseCoverage: { state: 'fresh', fetchedAt: '2026-10-08T08:00:00Z', partialData: false, data: { assignedUsers: 30, totalUsers: 42, percentage: 71 }, scope: 'tenant_wide_verified' },
      activity: { state: 'fresh', fetchedAt: '2026-10-08T08:00:00Z', partialData: false, data: { items: [{ action: 'User created', outcome: 'Succeeded', timestamp: '2026-10-08T08:00:00Z' }] }, scope: 'workspace' },
    });
    if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'connected', lastVerifiedAt: null });
    if (path === '/api/audit/events') return Response.json({ items: [] });
    if (path === '/api/devices?pageSize=1') return Response.json({ items: [{ id: 'one' }] });
    throw new Error(`Unexpected request: ${path}`);
  }));

  render(<OverviewPage session={{
    user: {}, workspace: { id: 'w', name: 'Customer', moduleAccess: ['users', 'licenses', 'devices'] },
    workspaceAccess: { role: 'workspace_owner', canManageMembers: true, canManageSettings: true },
  }} />);

  await waitFor(() => expect(screen.getByText('User created')).toBeTruthy());
  expect(requested).toEqual(['/api/overview']);
  expect(requested).not.toContain('/api/workspaces/current/connection-health');
  expect(requested).not.toContain('/api/audit/events');
  expect(requested).not.toContain('/api/devices?pageSize=1');
});
