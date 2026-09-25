import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';

const getApiToken = vi.hoisted(() => vi.fn(async () => 'test-token'));
vi.mock('../../../../src/Web/src/auth/AuthProvider', () => ({ useAuth: () => ({ getApiToken }) }));

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks(); });

it('requests overview without querying connection health or a device search page', async () => {
  const requested: string[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    requested.push(path);
    if (path === '/api/overview') return Response.json({
      freshness: 'live', fetchedAt: '2026-09-25T08:00:00Z', totalUsers: 42,
      licenseCoverage: { assigned: 30, available: 12, percentage: 71 },
      permissionHealth: { state: 'healthy', allowedCount: 4, totalCount: 4 },
      pimAttention: { requiresAttention: false, count: 0 }, partialData: false,
      access: { state: 'allowed' },
    });
    if (path === '/api/workspaces/current/connection-health') return Response.json({ status: 'connected', lastVerifiedAt: new Date().toISOString() });
    if (path === '/api/devices?pageSize=1') return Response.json({ items: [{ id: 'one' }], total: 1, access: { state: 'allowed' } });
    throw new Error(`Unexpected request: ${path}`);
  }));

  render(<OverviewPage session={{ user: {}, workspace: { id: 'w', name: 'Customer', moduleAccess: ['users', 'devices', 'licenses'] } }} />);

  await waitFor(() => expect(screen.getByText('No verified tenant total')).toBeTruthy());
  expect(requested).toContain('/api/overview');
  expect(requested).not.toContain('/api/workspaces/current/connection-health');
  expect(requested).not.toContain('/api/devices?pageSize=1');
});
