import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ExchangeOverviewPage } from '../../../../src/Web/src/features/exchange/ExchangeOverviewPage';

const { api } = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => api }));

const firstPage = { items: [{ userId: 'ada', displayName: 'Ada', address: 'ada@example.com' }], continuationToken: 'next-token', limitation: 'Directory-backed identities only.' };
const secondPage = { items: [{ userId: 'lin', displayName: 'Lin', address: 'lin@example.com' }], continuationToken: null };

describe('ExchangeOverviewPage', () => {
  afterEach(cleanup);
  beforeEach(() => api.mockReset());

  it('submits a search deliberately and pages back without changing the applied query', async () => {
    api.mockImplementation(async (path: string) => ({ ok: true, json: async () => path.includes('continuationToken=next-token') ? secondPage : firstPage }));
    render(<ExchangeOverviewPage />);
    await waitFor(() => expect(screen.getByText('Ada')).toBeTruthy());
    const search = screen.getByRole('textbox', { name: 'Search mailboxes' });
    fireEvent.change(search, { target: { value: 'Ada' } });
    expect(api).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));
    await waitFor(() => expect(api).toHaveBeenLastCalledWith(expect.stringContaining('search=Ada')));
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() => expect(screen.getByText('Lin')).toBeTruthy());
    expect(api).toHaveBeenLastCalledWith(expect.stringContaining('continuationToken=next-token'));
    expect(screen.getByText('Page 2')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Previous page' }));
    await waitFor(() => expect(screen.getByText('Ada')).toBeTruthy());
    expect(api).toHaveBeenLastCalledWith(expect.not.stringContaining('continuationToken='));
  });

  it('keeps directory failure separate from a mailbox verification failure and shows retrieval times', async () => {
    let attempts = 0;
    api.mockImplementation(async (path: string) => {
      if (String(path).includes('/overview')) throw new Error('private verification detail');
      if (++attempts === 1) throw new Error('private upstream detail');
      return { ok: true, json: async () => firstPage };
    });
    render(<ExchangeOverviewPage />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry mailboxes' })).toBeTruthy());
    expect(document.body.textContent).not.toContain('private upstream detail');
    fireEvent.click(screen.getByRole('button', { name: 'Retry mailboxes' }));
    await waitFor(() => expect(screen.getByText('Ada')).toBeTruthy());
    expect(screen.getByText(/Source: Microsoft Graph.*directory.*Retrieved:/)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Verify Exchange details for Ada' }));
    await waitFor(() => expect(screen.getByText('Mailbox details are temporarily unavailable.')).toBeTruthy());
    expect(screen.getByText(/Source: Microsoft Graph.*mailbox.*Not retrieved/i)).toBeTruthy();
    expect(screen.getByText('Ada')).toBeTruthy();
  });
});
