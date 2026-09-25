import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LicensesPage } from '../../../../src/Web/src/features/licenses/LicensesPage';
import type { UsersDirectoryResponse } from '../../../../src/Web/src/features/users/usersApi';

describe('LicensesPage', () => {
  afterEach(cleanup);

  it('shows purchased, assigned, available counts and paged roster for selected SKU', async () => {
    render(<LicensesPage loadLicenses={async () => ({
      items: [{ skuId: '11111111-1111-1111-1111-111111111111', partNumber: 'ENTERPRISEPACK', displayName: 'ENTERPRISEPACK', purchased: 10, assigned: 8, available: 2 }],
      total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' }
    })} loadAssignees={async () => ({ items: [{ id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: null, accountEnabled: true, userType: 'Member' }], continuationToken: 'next', fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false })} />);

    await waitFor(() => expect(screen.getByText('ENTERPRISEPACK')).toBeTruthy());
    fireEvent.click(screen.getByRole('tab', { name: 'Assigned users' }));
    expect(screen.getByText('10')).toBeTruthy();
    expect(screen.getByText('8')).toBeTruthy();
    expect(screen.getByText('2')).toBeTruthy();
    expect((await screen.findByRole('link', { name: 'Ada Lovelace' })).getAttribute('href')).toBe('/users/user-1');
    expect(screen.getByRole('button', { name: 'Next page' })).toBeTruthy();
  });

  it('shows unavailable and retry state', async () => {
    render(<LicensesPage loadLicenses={async () => { throw new Error('unavailable'); }} />);
    expect((await screen.findByRole('alert')).textContent).toContain('License data is unavailable');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });

  it('renders a safe empty-catalog state without inventing SKU identifiers', async () => {
    render(<LicensesPage loadLicenses={async () => ({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);

    expect(await screen.findByText('No license data is available.')).toBeTruthy();
    expect(screen.queryByText(/sku-/i)).toBeNull();
  });

  it('keeps catalog IDs as opaque values when rendering license choices', async () => {
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'opaque/sku?id=1', partNumber: 'E5', displayName: 'Microsoft 365 E5', purchased: 4, assigned: 0, available: 4 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);

    expect(await screen.findByText('Microsoft 365 E5')).toBeTruthy();
    expect(screen.getAllByText('4')).toHaveLength(2);
  });

  it('clears stale roster rows and blocks repeated paging until the next page arrives', async () => {
    let finishNext!: (value: UsersDirectoryResponse) => void;
    let finishPrevious!: (value: UsersDirectoryResponse) => void;
    let call = 0;
    const firstPage: UsersDirectoryResponse = { items: [{ id: 'user-1', displayName: 'Ada', userPrincipalName: 'ada@example.com', mail: null, accountEnabled: true, userType: 'Member' }], continuationToken: 'next', fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false };
    const loadAssignees = vi.fn((_skuId: string, _token: string | null) => {
      call++;
      return call === 1 ? Promise.resolve(firstPage) : new Promise<UsersDirectoryResponse>(resolve => {
        if (call === 2) finishNext = resolve;
        else finishPrevious = resolve;
      });
    });
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'sku-1', partNumber: 'E3', displayName: 'E3', purchased: 10, assigned: 2, available: 8 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} loadAssignees={loadAssignees} />);

    fireEvent.click(await screen.findByRole('button', { name: 'View assigned users' }));
    await screen.findByRole('link', { name: 'Ada' });
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(screen.queryByRole('link', { name: 'Ada' })).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Loading assigned users');
    expect((screen.getByRole('button', { name: 'Next page' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Previous page' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(loadAssignees).toHaveBeenCalledTimes(2);
    finishNext({ items: [{ id: 'user-2', displayName: 'Grace', userPrincipalName: 'grace@example.com', mail: null, accountEnabled: true, userType: 'Member' }], continuationToken: null, fetchedAt: '2026-09-21T10:01:00Z', freshness: 'fresh', partialData: false });
    expect(await screen.findByRole('link', { name: 'Grace' })).toBeTruthy();
    expect(screen.getByText('Page 2')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Previous page' }));
    expect(screen.queryByRole('link', { name: 'Grace' })).toBeNull();
    expect((screen.getByRole('button', { name: 'Previous page' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Previous page' }));
    expect(loadAssignees).toHaveBeenCalledTimes(3);
    finishPrevious(firstPage);
    expect(await screen.findByRole('link', { name: 'Ada' })).toBeTruthy();
    expect(screen.getByText('Page 1')).toBeTruthy();
    expect(loadAssignees.mock.calls.map(call => call[1])).toEqual([null, 'next', null]);
  });

  it('retries a failed roster page without reloading inventory or changing page', async () => {
    const loadLicenses = vi.fn(async () => ({ items: [{ skuId: 'sku-1', partNumber: 'E3', displayName: 'E3', purchased: 10, assigned: 2, available: 8 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } }));
    const loadAssignees = vi.fn().mockResolvedValueOnce({ items: [{ id: 'user-1', displayName: 'Ada', userPrincipalName: 'ada@example.com' }], continuationToken: 'next', fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false })
      .mockRejectedValueOnce(new Error('Graph unavailable'))
      .mockResolvedValueOnce({ items: [{ id: 'user-2', displayName: 'Grace', userPrincipalName: 'grace@example.com' }], continuationToken: null, fetchedAt: '2026-09-21T10:01:00Z', freshness: 'fresh', partialData: false });
    render(<LicensesPage loadLicenses={loadLicenses} loadAssignees={loadAssignees} />);

    fireEvent.click(await screen.findByRole('button', { name: 'View assigned users' }));
    await screen.findByRole('link', { name: 'Ada' });
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await screen.findByRole('alert');
    expect(screen.queryByRole('link', { name: 'Ada' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('link', { name: 'Grace' })).toBeTruthy();
    expect(screen.getByText('Page 2')).toBeTruthy();
    expect(loadAssignees).toHaveBeenCalledTimes(3);
    expect(loadLicenses).toHaveBeenCalledTimes(1);
  });

  it('exports the active SKU search and the selected SKU roster separately', async () => {
    const exportCsv = vi.fn(async () => ({ rowCount: 10000, maximum: 10000, truncated: true }));
    const loader = vi.fn(async () => ({ items: [{ skuId: '11111111-1111-1111-1111-111111111111', partNumber: 'E3', displayName: 'E3', purchased: 10, assigned: 7, available: 3 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } }));
    render(<LicensesPage loadLicenses={loader} loadAssignees={async () => ({ items: [], continuationToken: null, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false })} exportCsv={exportCsv} />);

    fireEvent.change(await screen.findByLabelText('Search SKUs'), { target: { value: 'E3' } });
    fireEvent.click(screen.getByRole('button', { name: 'Export filtered CSV' }));
    await waitFor(() => expect(exportCsv).toHaveBeenCalledWith('/api/licenses/export.csv?search=E3', 'licenses.csv'));
    expect(await screen.findByText(/10,000.*maximum.*truncated/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'View assigned users' }));
    fireEvent.click(screen.getByRole('button', { name: 'Export assignees CSV' }));
    await waitFor(() => expect(exportCsv).toHaveBeenCalledWith('/api/licenses/11111111-1111-1111-1111-111111111111/assignees/export.csv', 'license-assignees.csv'));
  });
});
