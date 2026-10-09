import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LicensesPage, type LicenseOverview } from '../../../../src/Web/src/features/licenses/LicensesPage';
import type { UsersDirectoryResponse } from '../../../../src/Web/src/features/users/usersApi';

const issueReport = vi.fn();
const issueClear = vi.fn();
const reporter = { report: issueReport, clear: issueClear };
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => reporter }));

describe('LicensesPage', () => {
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); issueReport.mockClear(); issueClear.mockClear(); });

  it('shows a friendly name with the original part number and SKU ID', async () => {
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'sku-1', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 10, assigned: 7, available: 3 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);
    const row = (await screen.findByText('Office 365 E3')).closest('tr');
    expect(row?.textContent).toContain('ENTERPRISEPACK');
    expect(row?.textContent).toContain('sku-1');
    expect(row?.textContent).not.toContain('Product name unavailable');
    expect(screen.getByRole('button', { name: 'View assigned users for Office 365 E3' })).toBeTruthy();
  });

  it('shows the hygiene entry only when capability and both workspace module grants allow it', async () => {
    render(<LicensesPage
      loadLicenses={async () => ({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })}
      capabilities={[{ capability: 'licenses.hygiene.view', state: 'read_only', reasonCode: 'role_read_only' }]}
      moduleEnabled={['licenses', 'license-hygiene']}
      moduleAssigned={['licenses', 'license-hygiene']}
    />);

    expect((await screen.findByRole('link', { name: 'Review license hygiene' })).getAttribute('href')).toBe('/licenses/hygiene');
  });

  it('hides the hygiene entry if its capability or either module grant is missing', async () => {
    const { rerender } = render(<LicensesPage
      loadLicenses={async () => ({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })}
      capabilities={[{ capability: 'licenses.hygiene.view', state: 'hidden', reasonCode: 'role_required' }]}
      moduleEnabled={['licenses', 'license-hygiene']}
      moduleAssigned={['licenses', 'license-hygiene']}
    />);
    expect(await screen.findByText('No license data is available.')).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Review license hygiene' })).toBeNull();

    rerender(<LicensesPage
      loadLicenses={async () => ({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })}
      capabilities={[{ capability: 'licenses.hygiene.view', state: 'allowed', reasonCode: 'active_role' }]}
      moduleEnabled={['licenses', 'license-hygiene']}
      moduleAssigned={['licenses']}
    />);
    expect(screen.queryByRole('link', { name: 'Review license hygiene' })).toBeNull();
  });

  it('keeps an unknown long code visible and explicitly marks its product name unavailable', async () => {
    const code = 'LONG_UNKNOWN_PRODUCT_CODE_WITH_MANY_SEGMENTS_2026';
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'opaque-sku', partNumber: code, displayName: code, purchased: 1, assigned: 0, available: 1 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);
    const row = (await screen.findAllByText(code))[0].closest('tr');
    expect(row?.textContent).toContain('Unrecognised product');
    expect(row?.textContent).not.toContain('Product name unavailable');
    expect(row?.textContent).toContain('opaque-sku');
  });

  it('renders null display names as the part number, with numeric cells, usage meter and no retrieved line', async () => {
    const part = 'SOME_VERY_LONG_UNKNOWN_PART_NUMBER_FOR_TESTING_12345';
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: '11111111-2222-3333-4444-555555555555', partNumber: part, displayName: null as unknown as string, purchased: 10, assigned: 8, available: 2 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);
    const row = (await screen.findAllByText(part))[0].closest('tr')!;
    expect(row.textContent).toContain('Unrecognised product');
    expect(row.textContent).not.toContain('Product name unavailable');
    expect(row.querySelectorAll('td.numeric')).toHaveLength(3);
    expect(screen.getByText('8 of 10 (80%)')).toBeTruthy();
    const meter = row.querySelector('meter')!;
    expect(meter.getAttribute('value')).toBe('8');
    expect(meter.getAttribute('max')).toBe('10');
    expect(document.querySelector('[aria-label="License inventory"]')).toBeNull();
    expect(document.body.textContent).not.toContain('Retrieved');
    expect(row.querySelector('details')?.textContent).toContain('11111111-2222-3333-4444-555555555555');
  });

  it('lets users pick the license on the assigned-users tab', async () => {
    const loadAssignees = vi.fn(async (): Promise<UsersDirectoryResponse> => ({ items: [], total: 0, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } } as unknown as UsersDirectoryResponse));
    render(<LicensesPage loadAssignees={loadAssignees} loadLicenses={async () => ({ items: [
      { skuId: 'sku-a', partNumber: 'A', displayName: 'Alpha plan', purchased: 5, assigned: 1, available: 4 },
      { skuId: 'sku-b', partNumber: 'B', displayName: 'Beta plan', purchased: 5, assigned: 1, available: 4 },
    ], total: 2, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);
    fireEvent.click(await screen.findByRole('tab', { name: 'Assigned users' }));
    const select = await screen.findByRole('combobox', { name: 'License' }) as HTMLSelectElement;
    expect(Array.from(select.options).map(option => option.text)).toEqual(['Alpha plan', 'Beta plan']);
    fireEvent.change(select, { target: { value: 'sku-b' } });
    await waitFor(() => expect(loadAssignees).toHaveBeenLastCalledWith('sku-b', null));
    expect(await screen.findByRole('heading', { name: 'Users assigned Beta plan' })).toBeTruthy();
  });

  it('uses the compact result view with counts and roster action at narrow widths', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    render(<LicensesPage loadLicenses={async () => ({ items: [{ skuId: 'sku-1', partNumber: 'SPE_E5', displayName: 'Microsoft 365 E5', purchased: 10, assigned: 7, available: 3 }], total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } })} />);
    const list = await screen.findByRole('list', { name: 'Licenses' });
    expect(list.textContent).toContain('Microsoft 365 E5');
    expect(list.textContent).toContain('SPE_E5');
    expect(list.textContent).toContain('sku-1');
    expect(list.textContent).toContain('10');
    expect(list.textContent).toContain('7');
    expect(list.textContent).toContain('3');
    expect(screen.getByRole('button', { name: 'View assigned users for Microsoft 365 E5' })).toBeTruthy();
  });

  it('retains the page heading and reports an unavailable inventory read', async () => {
    render(<LicensesPage loadLicenses={async () => { throw new Error('Graph unavailable'); }} />);
    expect(await screen.findByRole('heading', { name: 'License inventory' })).toBeTruthy();
    expect(screen.getByRole('alert').textContent).toContain('License data is unavailable');
    await waitFor(() => expect(issueReport).toHaveBeenCalledWith(expect.objectContaining({ key: 'licenses:read', area: 'licenses', kind: 'service' })));
  });

  it('shows denied access without reporting it as a service failure', async () => {
    render(<LicensesPage loadLicenses={async () => ({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'unavailable', partialData: true, access: { state: 'consent_required' }, error: { message: 'Capability required' } })} />);
    expect(await screen.findByText(/permission/i)).toBeTruthy();
    expect(issueReport).not.toHaveBeenCalled();
  });

  it('shows purchased, assigned, available counts and paged roster for selected SKU', async () => {
    render(<LicensesPage loadLicenses={async () => ({
      items: [{ skuId: '11111111-1111-1111-1111-111111111111', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 10, assigned: 8, available: 2 }],
      total: 1, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' }
    })} loadAssignees={async () => ({ items: [{ id: 'user-1', displayName: 'Ada Lovelace', userPrincipalName: 'ada@example.com', mail: null, accountEnabled: true, userType: 'Member' }], continuationToken: 'next', fetchedAt: '2026-09-21T10:00:00Z', freshness: 'fresh', partialData: false })} />);

    await waitFor(() => expect(screen.getByText('Office 365 E3')).toBeTruthy());
    fireEvent.click(screen.getByRole('tab', { name: 'Assigned users' }));
    expect(screen.getByText('10')).toBeTruthy();
    expect(screen.getByText('8')).toBeTruthy();
    expect(screen.getByText('2')).toBeTruthy();
    expect((await screen.findByRole('link', { name: 'Ada Lovelace' })).getAttribute('href')).toBe('/users/user-1');
    const rosterRow = screen.getByRole('link', { name: 'Ada Lovelace' }).closest('tr');
    expect(rosterRow?.querySelector('th')?.getAttribute('data-label')).toBe('User');
    expect(rosterRow?.querySelector('td')?.getAttribute('data-label')).toBe('User principal name');
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

  it('hides old inventory rows and roster actions while a new search is loading', async () => {
    let finishSearch!: (value: LicenseOverview) => void;
    const loader = vi.fn((search = '', page = 1) => search || page !== 1
      ? new Promise<LicenseOverview>((resolve) => { finishSearch = resolve; })
      : Promise.resolve({
        items: [{ skuId: 'old-sku', partNumber: 'OLD', displayName: 'Old license', purchased: 10, assigned: 4, available: 6 }],
        total: 50, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }));
    render(<LicensesPage loadLicenses={loader} />);

    expect(await screen.findByText('Old license')).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Search SKUs'), { target: { value: 'new' } });

    expect(screen.queryByText('Old license')).toBeNull();
    expect(screen.queryByRole('button', { name: 'View assigned users' })).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Loading licenses');
    finishSearch({ items: [], total: 0, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:01:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } });
  });

  it('hides old inventory rows while a new page is loading', async () => {
    let finishPage!: (value: LicenseOverview) => void;
    const loader = vi.fn((search = '', page = 1) => page === 1
      ? Promise.resolve({
        items: [{ skuId: 'page-one-sku', partNumber: 'ONE', displayName: 'Page one license', purchased: 10, assigned: 4, available: 6 }],
        total: 50, page: 1, pageSize: 25, fetchedAt: '2026-09-21T10:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } },)
      : new Promise<LicenseOverview>((resolve) => { finishPage = resolve; }));
    render(<LicensesPage loadLicenses={loader} />);

    expect(await screen.findByText('Page one license')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    expect(screen.queryByText('Page one license')).toBeNull();
    expect(screen.queryByRole('button', { name: 'View assigned users' })).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Loading licenses');
    finishPage({ items: [], total: 50, page: 2, pageSize: 25, fetchedAt: '2026-09-21T10:01:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } });
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

    fireEvent.click(await screen.findByRole('button', { name: /View assigned users for/ }));
    await screen.findByRole('link', { name: 'Ada' });
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(screen.queryByRole('link', { name: 'Ada' })).toBeNull();
    expect(screen.getByText(/Loading assigned users/)).toBeTruthy();
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

    fireEvent.click(await screen.findByRole('button', { name: /View assigned users for/ }));
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
    fireEvent.click(await screen.findByRole('button', { name: 'Export filtered CSV' }));
    await waitFor(() => expect(exportCsv).toHaveBeenCalledWith('/api/licenses/export.csv?search=E3', 'licenses.csv'));
    expect(await screen.findByText(/10,000.*maximum.*truncated/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /View assigned users for/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Export assignees CSV' }));
    await waitFor(() => expect(exportCsv).toHaveBeenCalledWith('/api/licenses/11111111-1111-1111-1111-111111111111/assignees/export.csv', 'license-assignees.csv'));
  });
});
