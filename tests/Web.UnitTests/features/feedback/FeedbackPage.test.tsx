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
          { id: 'z-id', category: 'Bug', subject: 'Newer', message: 'New message', createdAt: '2026-10-02T10:00:00Z', expiresAt: '2027-01-02T10:00:00Z' },
          { id: 'a-id', category: 'General', subject: '<b>Older</b>', message: '<img src=x onerror=alert(1)>', createdAt: '2026-10-02T10:00:00Z', expiresAt: '2027-01-02T10:00:00Z' },
        ],
        nextCursor: 'next-page',
      }))
      .mockResolvedValueOnce(Response.json({
        items: [{ id: 'oldest', category: 'Improvement', subject: 'Oldest', message: 'Oldest message', createdAt: '2026-10-01T10:00:00Z', expiresAt: '2027-01-01T10:00:00Z' }],
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
    expect(apiMock.mock.calls[1][0]).toContain('cursor=next-page');
    const titles = Array.from(document.querySelectorAll('[data-feedback-subject]')).map(node => node.textContent);
    expect(titles).toEqual(['Newer', '<b>Older</b>', 'Oldest']);
  });

  it('formats submission dates with the shared date formatter', async () => {
    const createdAt = '2026-10-09T08:00:00Z';
    apiMock.mockResolvedValue(Response.json({
      items: [{ id: 'date-entry', category: 'General', subject: 'Dated entry', message: 'Message', createdAt, expiresAt: '2027-01-07T08:00:00Z' }],
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

  it('discards an earlier response after a refresh starts', async () => {
    let resolveFirst!: (response: Response) => void;
    apiMock
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(Response.json({
        items: [{ id: 'current', category: 'General', subject: 'Current results', message: 'Current', createdAt: '2026-10-03T10:00:00Z', expiresAt: '2027-01-03T10:00:00Z' }],
        nextCursor: null,
      }));
    const { rerender } = render(<FeedbackPage refreshRevision={0} onOpenFeedbackDialog={vi.fn()} />);
    rerender(<FeedbackPage refreshRevision={1} onOpenFeedbackDialog={vi.fn()} />);
    await screen.findByText('Current results');
    resolveFirst(Response.json({
      items: [{ id: 'stale', category: 'General', subject: 'Stale results', message: 'Stale', createdAt: '2026-10-01T10:00:00Z', expiresAt: '2027-01-01T10:00:00Z' }],
      nextCursor: null,
    }));

    await waitFor(() => expect(screen.queryByText('Stale results')).toBeNull());
    expect(screen.getByText('Current results')).toBeTruthy();
  });
});
