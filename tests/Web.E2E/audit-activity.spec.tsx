import React from 'react';
import { cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuditActivityPage } from '../../src/Web/src/features/audit/AuditActivityPage';

const apiFetch = vi.hoisted(() => async (path: string, init?: RequestInit) => window.fetch(path, init));

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiFetch,
}));

describe('audit activity browser boundary', () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('loads workspace audit activity through the same-origin API only', async () => {
    const requests: string[] = [];
    vi.spyOn(window, 'fetch').mockImplementation(async (input) => {
      const path = String(input);
      requests.push(path);
      expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
      expect(path).toBe('/api/audit/events?pageSize=25');
      expect(path).not.toMatch(/graph\.microsoft\.com/i);
      return new Response(JSON.stringify({
        items: [{
          id: 'audit-1',
          workspaceId: 'workspace-1',
          tenantId: 'tenant-1',
          actorTenantId: 'tenant-1',
          actorObjectId: 'actor-1',
          action: 'users.update',
          targetType: 'user',
          targetId: 'grace@example.com',
          outcome: 'failed',
          timestamp: '2026-09-21T09:00:00Z',
          correlationId: 'safe-correlation-456',
          graphCorrelationId: 'graph-correlation-456',
          graphRequestId: 'graph-request-456',
          pimRequestId: null,
          failureCategory: 'throttled',
          safeMetadataJson: '{"token":"[REDACTED]"}',
        }],
        fetchedAt: '2026-09-21T09:01:00Z',
        freshness: 'fresh',
        partialData: false,
        authoritativeSourceNotice: 'Microsoft 365 audit logs remain authoritative.',
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    render(<AuditActivityPage />);

    await waitFor(() => expect(document.body.textContent).toContain('Updated user'));
    expect(document.body.textContent).toContain('safe-correlation-456');
    expect(document.body.textContent).toContain('[REDACTED]');
    expect(requests).toEqual(['/api/audit/events?pageSize=25']);
  });
});
