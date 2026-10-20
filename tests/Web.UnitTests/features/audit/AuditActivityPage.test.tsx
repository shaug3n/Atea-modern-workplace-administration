import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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
  authoritativeSourceNotice: 'use Microsoft 365 audit logs.',
  nextContinuationToken: null,
};

describe('AuditActivityPage', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers(); });

  async function flushPromises() {
    await act(async () => {
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
    });
  }

  it('renders workspace audit events with safe correlation references', async () => {
    render(<AuditActivityPage loadAuditEvents={async () => response} />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Platform activity' })).toBeTruthy());
    expect(screen.getByText('Disabled user')).toBeTruthy();
    expect(screen.getAllByText('ada@example.com').length).toBeGreaterThan(0);
    expect(document.body.textContent).toContain('safe-correlation-123');
    expect(document.body.textContent).toContain('use Microsoft 365 audit logs');
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
    expect(screen.getByText('May be out of date')).toBeTruthy();

    cleanup();
    render(<AuditActivityPage loadAuditEvents={async () => { throw new Error('raw Graph payload with access_token'); }} />);

    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Audit activity is unavailable. Try again later.'));
    expect(document.body.textContent).not.toContain('raw Graph payload');
    expect(document.body.textContent).not.toContain('access_token');
  });

  it('marks_the_activity_loading_state_busy_until_the_first_response', async () => {
    let finish!: (value: AuditEventsResponse) => void;
    const pending = new Promise<AuditEventsResponse>(resolve => { finish = resolve; });
    render(<AuditActivityPage loadAuditEvents={() => pending} />);

    expect(screen.getByRole('status').getAttribute('aria-busy')).toBe('true');
    await act(async () => { finish(response); });
    expect(await screen.findByText('Disabled user')).toBeTruthy();
  });

  it('debounces_text_filters_for_300ms_and_resets_pagination_after_apply', async () => {
    vi.useFakeTimers();
    const loadAuditEvents = vi.fn()
      .mockResolvedValueOnce({ ...response, nextContinuationToken: 'page-2' })
      .mockResolvedValue({ ...response, nextContinuationToken: 'page-3' });
    render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await flushPromises();
    expect(loadAuditEvents).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Load next page' }));
    await flushPromises();
    expect(loadAuditEvents).toHaveBeenCalledTimes(2);
    expect(loadAuditEvents.mock.calls[1][0]).toMatchObject({ continuationToken: 'page-2' });

    fireEvent.change(screen.getByRole('textbox', { name: 'Person' }), { target: { value: 'actor-final' } });
    fireEvent.change(screen.getByRole('textbox', { name: /Action/ }), { target: { value: 'action-final' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(299); });
    expect(loadAuditEvents).toHaveBeenCalledTimes(2);
    expect(loadAuditEvents.mock.calls[1][0]).toMatchObject({ continuationToken: 'page-2' });

    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    await flushPromises();
    expect(loadAuditEvents).toHaveBeenCalledTimes(3);
    expect(loadAuditEvents.mock.calls[2][0]).toMatchObject({
      actorObjectId: 'actor-final',
      action: 'action-final',
      continuationToken: null,
    });
  });

  it('applies_outcome_immediately_with_the_visible_text_draft', async () => {
    vi.useFakeTimers();
    const loadAuditEvents = vi.fn()
      .mockResolvedValueOnce({ ...response, nextContinuationToken: 'page-2' })
      .mockResolvedValue(response);
    render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await flushPromises();

    fireEvent.change(screen.getByRole('textbox', { name: 'Person' }), { target: { value: 'draft-actor' } });
    fireEvent.change(screen.getByRole('textbox', { name: /Action/ }), { target: { value: 'draft-action' } });
    expect(loadAuditEvents).toHaveBeenCalledTimes(1);
    fireEvent.change(screen.getByRole('combobox', { name: /Outcome/ }), { target: { value: 'denied' } });
    await flushPromises();

    expect(loadAuditEvents).toHaveBeenCalledTimes(2);
    expect(loadAuditEvents.mock.calls[1][0]).toMatchObject({
      actorObjectId: 'draft-actor',
      action: 'draft-action',
      outcome: 'denied',
      continuationToken: null,
    });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(loadAuditEvents).toHaveBeenCalledTimes(2);
  });

  it('ignores_stale_filter_responses', async () => {
    vi.useFakeTimers();
    let finishOld!: (value: AuditEventsResponse) => void;
    let finishNew!: (value: AuditEventsResponse) => void;
    const oldResponse = new Promise<AuditEventsResponse>(resolve => { finishOld = resolve; });
    const newResponse = new Promise<AuditEventsResponse>(resolve => { finishNew = resolve; });
    const loadAuditEvents = vi.fn()
      .mockResolvedValueOnce(response)
      .mockReturnValueOnce(oldResponse)
      .mockReturnValueOnce(newResponse);
    render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await flushPromises();

    const action = screen.getByRole('textbox', { name: /Action/ });
    fireEvent.change(action, { target: { value: 'old' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(loadAuditEvents).toHaveBeenCalledTimes(2);
    fireEvent.change(action, { target: { value: 'new' } });
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(loadAuditEvents).toHaveBeenCalledTimes(3);

    await act(async () => { finishNew({ ...response, items: [{ ...response.items[0], targetId: 'new-result' }] }); });
    await flushPromises();
    expect(screen.getAllByText('new-result').length).toBeGreaterThan(0);
    await act(async () => { finishOld({ ...response, items: [{ ...response.items[0], targetId: 'old-result' }] }); });
    await flushPromises();
    expect(screen.getAllByText('new-result').length).toBeGreaterThan(0);
    expect(screen.queryByText('old-result')).toBeNull();
  });

  it('cancels_pending_filter_debounce_on_unmount', async () => {
    vi.useFakeTimers();
    const loadAuditEvents = vi.fn().mockResolvedValue(response);
    const { unmount } = render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await flushPromises();
    fireEvent.change(screen.getByRole('textbox', { name: 'Person' }), { target: { value: 'pending' } });
    unmount();
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(loadAuditEvents).toHaveBeenCalledTimes(1);
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
    await waitFor(() => expect(screen.getByText('Disabled user')).toBeTruthy());
    expect(attempts).toBe(2);
  });

  it('does not show an old freshness banner while the next audit page is loading', async () => {
    let finishPage: ((value: AuditEventsResponse) => void) | undefined;
    const loadAuditEvents = async (filters?: { continuationToken?: string | null }) => filters?.continuationToken
      ? new Promise<AuditEventsResponse>(resolve => { finishPage = resolve; })
      : { ...response, nextContinuationToken: 'page-2' };
    render(<AuditActivityPage loadAuditEvents={loadAuditEvents} />);
    await waitFor(() => expect(screen.getByText('Disabled user')).toBeTruthy());
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
    expect(list.querySelector('details')?.textContent).toContain(longReference);
    expect(screen.getByText(/partial results/i)).toBeTruthy();
    expect(document.querySelector('.audit-filters')?.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('reveals failure category and safe metadata from a compact audit record', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<AuditActivityPage loadAuditEvents={async () => ({ ...response, items: [{ ...response.items[0], failureCategory: 'role_required' }] })} />);
    const record = (await screen.findByRole('list', { name: 'Audit activity' })).querySelector('li')!;
    fireEvent.click(screen.getByText('Details'));
    expect(record.textContent).toContain('role_required');
    expect(record.textContent).toContain('{"reason":"reviewed"}');
  });

  it('keeps technical ids and JSON out of the visible row until Details is expanded', async () => {
    render(<AuditActivityPage loadAuditEvents={async () => response} />);
    const row = (await screen.findByText('Disabled user')).closest('tr')!;
    const visible = Array.from(row.querySelectorAll('td')).filter(cell => !cell.querySelector('details')).map(cell => cell.textContent).join(' ');
    expect(visible).not.toContain('safe-correlation-123');
    expect(visible).not.toContain('actor-1');
    expect(visible).not.toMatch(/[{}]/);
    const details = row.querySelector('details')!;
    expect(details.textContent).toContain('safe-correlation-123');
    expect(details.textContent).toContain('{"reason":"reviewed"}');
  });

  it('humanizes unknown actions and uses a Person filter and info note', async () => {
    render(<AuditActivityPage loadAuditEvents={async () => ({ ...response, items: [{ ...response.items[0], action: 'foo.bar_baz', targetId: '11111111-2222-3333-4444-555555555555', targetType: 'group' }] })} />);
    expect(await screen.findByText('Foo bar baz')).toBeTruthy();
    expect(screen.queryByText('foo.bar_baz')).toBeNull();
    expect(screen.getByText('Unnamed group')).toBeTruthy();
    expect(screen.getByLabelText('Person')).toBeTruthy();
    expect(screen.queryByLabelText('Actor object ID')).toBeNull();
    expect(screen.getByText(/Shows actions taken in Atea Unified Workplace/)).toBeTruthy();
    expect(document.querySelector('td .status-badge, td [class*="status-badge"]')?.textContent).toContain('Succeeded');
  });
});
