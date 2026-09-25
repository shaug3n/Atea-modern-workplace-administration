import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuditActivityPage, type AuditEventsResponse } from '../../../../src/Web/src/features/audit/AuditActivityPage';

const response: AuditEventsResponse = {
  items: [
    {
      id: 'audit-1',
      workspaceId: 'workspace-1',
      tenantId: 'tenant-1',
      actorTenantId: 'tenant-1',
      actorObjectId: 'actor-1',
      action: 'users.disable',
      targetType: 'user',
      targetId: 'ada@example.com',
      outcome: 'succeeded',
      timestamp: '2026-09-21T08:30:00Z',
      correlationId: 'safe-correlation-123',
      graphCorrelationId: null,
      graphRequestId: 'graph-request-123',
      pimRequestId: null,
      failureCategory: null,
      safeMetadataJson: '{"reason":"reviewed"}',
    },
  ],
  fetchedAt: '2026-09-21T08:31:00Z',
  freshness: 'fresh',
  partialData: false,
  authoritativeSourceNotice: 'Microsoft 365 audit logs remain authoritative.',
  nextContinuationToken: null,
};

describe('AuditActivityPage', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

  it('renders workspace audit events with safe correlation references', async () => {
    render(<AuditActivityPage loadAuditEvents={async () => response} />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Audit activity' })).toBeTruthy());
    expect(screen.getByText('users.disable')).toBeTruthy();
    expect(screen.getByText('ada@example.com')).toBeTruthy();
    expect(document.body.textContent).toContain('safe-correlation-123');
    expect(document.body.textContent).toContain('Microsoft 365 audit logs remain authoritative');
    expect(document.body.textContent).not.toContain('access_token');
  });

  it('shows loading, empty, stale and error states with audit-specific messages', async () => {
    render(<AuditActivityPage loadAuditEvents={async () => ({
      ...response,
      items: [],
      freshness: 'stale',
      partialData: true,
    })} />);

    expect(screen.getByRole('status').textContent).toContain('Loading audit activity');
    await waitFor(() => expect(screen.getByText('No audit activity is available for this workspace yet.')).toBeTruthy());
    expect(screen.getByRole('alert').textContent).toContain('Audit data may be stale');

    cleanup();
    render(<AuditActivityPage loadAuditEvents={async () => { throw new Error('raw Graph payload with access_token'); }} />);

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Audit activity is unavailable. Try again later.'));
    expect(document.body.textContent).not.toContain('raw Graph payload');
    expect(document.body.textContent).not.toContain('access_token');
  });

  it('retries audit loading after a transient read failure', async () => {
    let attempts = 0;
    render(<AuditActivityPage loadAuditEvents={async () => {
      attempts += 1;
      if (attempts === 1) throw new Error('transient audit failure');
      return response;
    }} />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy());
    screen.getByRole('button', { name: 'Retry' }).click();
    await waitFor(() => expect(screen.getByText('users.disable')).toBeTruthy());
    expect(attempts).toBe(2);
  });

  it('does not show an old freshness banner while the next audit page is loading', async () => {
    let finishPage: ((value: AuditEventsResponse) => void) | undefined;
    const loadAuditEvents = async (filters?: { continuationToken?: string | null }) => filters?.continuationToken
      ? new Promise<AuditEventsResponse>(resolve => { finishPage = resolve; })
      : { ...response, nextContinuationToken: 'page-2' };
    render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await waitFor(() => expect(screen.getByText('users.disable')).toBeTruthy());
    screen.getByRole('button', { name: 'Load next page' }).click();
    await waitFor(() => expect(screen.getByText('Loading audit activity…')).toBeTruthy());
    expect(screen.queryByText(/Fetched:/)).toBeNull();
    finishPage?.({ ...response, items: [], fetchedAt: '2026-09-22T10:00:00Z', nextContinuationToken: null });
    await waitFor(() => expect(screen.getByText('No audit activity is available for this workspace yet.')).toBeTruthy());
  });

  it('places filters before partial results and wraps long references in compact summaries', async () => {
    const longReference = 'reference-' + 'x'.repeat(100);
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<AuditActivityPage loadAuditEvents={async () => ({ ...response, partialData: true, items: [{ ...response.items[0], graphRequestId: longReference }] })} />);
    const list = await screen.findByRole('list', { name: 'Audit activity' });
    expect(list.textContent).toContain(longReference);
    expect(list.querySelector('code')?.className).toContain('audit-reference');
    expect(screen.getByText(/partial results/i)).toBeTruthy();
    expect(document.querySelector('.audit-filters')?.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
