import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FeedbackPage } from '../../../../src/Web/src/features/feedback/FeedbackPage';
import { formatDate } from '../../../../src/Web/src/format/dateTime';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

describe('FeedbackPage', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('shows loading, then a friendly empty state with the shared dialog action', async () => {
    apiMock.mockResolvedValue(Response.json({ items: [], nextCursor: null }));
    const openDialog = vi.fn();
    render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={openDialog} />);

    expect(screen.getByRole('status').textContent).toContain('Loading');
    fireEvent.click(await screen.findByRole('button', { name: 'Give feedback' }));

    expect(openDialog).toHaveBeenCalledOnce();
  });

  it('preserves the server newest-first order and loads the next cursor page', async () => {
    apiMock
      .mockResolvedValueOnce(Response.json({
        items: [
          { id: '11111111-1111-4111-8111-111111111111', category: 'Bug', subject: 'Newer', message: 'New message', createdAt: '2026-10-02T10:00:00Z', expiresAt: '2027-01-02T10:00:00Z' },
          { id: '22222222-2222-4222-8222-222222222222', category: 'General', subject: '<b>Older</b>', message: '<img src=x onerror=alert(1)>', createdAt: '2026-10-02T10:00:00Z', expiresAt: '2027-01-02T10:00:00Z' },
        ],
        nextCursor: 'NjM5MjY1MzIwMDAwMDAwMDAwOjExMTExMTExMTExMTQxMTE4MTExMTExMTExMTExMTEx',
      }))
      .mockResolvedValueOnce(Response.json({
        items: [{ id: '33333333-3333-4333-8333-333333333333', category: 'Improvement', subject: 'Oldest', message: 'Oldest message', createdAt: '2026-10-01T10:00:00Z', expiresAt: '2027-01-01T10:00:00Z' }],
        nextCursor: null,
      }));
    render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);

    const older = await screen.findByText('<b>Older</b>');
    expect(older.closest('article')?.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(older.querySelector('b')).toBeNull();
    expect(older.closest('article')?.querySelector('img')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Load more feedback' }));

    await screen.findByText('Oldest');
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(2));
    expect(apiMock.mock.calls[1][0]).toContain('cursor=NjM5MjY1MzIwMDAwMDAwMDAwOjExMTExMTExMTExMTQxMTE4MTExMTExMTExMTExMTEx');
    const titles = Array.from(document.querySelectorAll('[data-feedback-subject]')).map(node => node.textContent);
    expect(titles).toEqual(['Newer', '<b>Older</b>', 'Oldest']);
  });

  it('formats submission dates with the shared date formatter', async () => {
    const createdAt = '2026-10-09T08:00:00Z';
    apiMock.mockResolvedValue(Response.json({
      items: [{ id: '44444444-4444-4444-8444-444444444444', category: 'General', subject: 'Dated entry', message: 'Message', createdAt, expiresAt: '2027-01-07T08:00:00Z' }],
      nextCursor: null,
    }));
    render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);

    await screen.findByText('Dated entry');
    const date = document.querySelector('time')!;
    expect(date.textContent).toContain(formatDate(createdAt));
    expect(date.tagName).toBe('TIME');
    expect(date.getAttribute('datetime')).toBe(createdAt);
  });

  it('shows an actionable error instead of an empty list when the response is invalid', async () => {
    apiMock.mockResolvedValue(new Response('not json', { status: 200 }));
    render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);

    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be loaded/i);
    expect(screen.queryByText(/no feedback yet/i)).toBeNull();
  });

  it('distinguishes expired authentication from network and server list failures', async () => {
    apiMock.mockResolvedValueOnce(Response.json({ error: 'authorization_denied' }, { status: 401 }));
    const { rerender } = render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);
    expect((await screen.findByRole('alert')).textContent).toMatch(/sign in again/i);

    apiMock.mockResolvedValueOnce(Response.json({ error: 'persistence_unavailable' }, { status: 503 }));
    rerender(<FeedbackPage refreshRevision={1} onOpenFeedbackDialog={vi.fn()} />);
    expect((await screen.findByRole('alert')).textContent).toMatch(/try again later/i);
  });

  it('discards an earlier response after a refresh starts', async () => {
    let resolveFirst!: (response: Response) => void;
    apiMock
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(Response.json({
        items: [{ id: '55555555-5555-4555-8555-555555555555', category: 'General', subject: 'Current results', message: 'Current', createdAt: '2026-10-03T10:00:00Z', expiresAt: '2027-01-03T10:00:00Z' }],
        nextCursor: null,
      }));
    const { rerender } = render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);
    rerender(<FeedbackPage refreshRevision={1} onOpenFeedbackDialog={vi.fn()} />);
    await screen.findByText('Current results');
    resolveFirst(Response.json({
      items: [{ id: '66666666-6666-4666-8666-666666666666', category: 'General', subject: 'Stale results', message: 'Stale', createdAt: '2026-10-01T10:00:00Z', expiresAt: '2027-01-01T10:00:00Z' }],
      nextCursor: null,
    }));

    await waitFor(() => expect(screen.queryByText('Stale results')).toBeNull());
    expect(screen.getByText('Current results')).toBeTruthy();
  });
});
