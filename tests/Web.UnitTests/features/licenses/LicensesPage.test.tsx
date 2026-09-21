import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LicensesPage } from '../../../../src/Web/src/features/licenses/LicensesPage';

describe('LicensesPage', () => {
  afterEach(cleanup);

  it('renders assigned and available counts with safe user links', async () => {
    render(<LicensesPage loadLicenses={async () => ({
      items: [{ skuId: 'sku-1', partNumber: 'E3', displayName: 'Microsoft 365 E3', assigned: 8, available: 2, affectedUsers: [{ id: 'user-1', displayName: 'Ada Lovelace' }] }],
      total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' }
    })} />);

    await waitFor(() => expect(screen.getByText('Microsoft 365 E3')).toBeTruthy());
    expect(screen.getByText('8')).toBeTruthy();
    expect(screen.getByText('2')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Ada Lovelace' }).getAttribute('href')).toBe('/users/user-1');
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
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'opaque/sku?id=1', partNumber: 'E5', displayName: 'Microsoft 365 E5', assigned: 0, available: 4 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);

    expect(await screen.findByText('Microsoft 365 E5')).toBeTruthy();
    expect(screen.getByText('4')).toBeTruthy();
  });
});
