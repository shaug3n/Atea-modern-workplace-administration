import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchLicenseHygiene, LicenseHygienePage, type LicenseHygieneResponse } from '../../../../src/Web/src/features/licenses/hygiene/LicenseHygienePage';

const stamp = '2026-10-08T10:00:00Z';

function response(overrides: Partial<LicenseHygieneResponse> = {}): LicenseHygieneResponse {
  return {
    access: { state: 'allowed' },
    inventory: { freshness: 'live', partialData: false, fetchedAt: stamp, error: null },
    userEvidence: { freshness: 'live', partialData: false, fetchedAt: stamp, error: null },
    coverage: { recordsAssessed: 3, completed: true, stopReason: 'completed', missingEvidenceRecords: 0 },
    capacityItems: [
      { skuId: 'sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 20, assigned: 15, available: 5 },
      { skuId: 'sku-e5', partNumber: 'SPE_E5', displayName: 'Microsoft 365 E5', purchased: 10, assigned: 8, available: 2 },
    ],
    disabledAccounts: [
      {
        id: 'user-ada',
        displayName: 'Ada Lovelace',
        userPrincipalName: 'ada@example.com',
        assignedLicenses: [{ skuId: 'sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3' }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph user assignment',
      },
      {
        id: 'user-grace',
        displayName: 'Grace Hopper',
        userPrincipalName: 'grace@example.com',
        assignedLicenses: [{ skuId: 'unknown-sku', partNumber: null, displayName: null }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph user assignment',
      },
    ],
    ...overrides,
  };
}

describe('LicenseHygienePage', () => {
  afterEach(() => {
    cleanup();
    window.history.pushState(null, '', '/');
  });

  it('fetches only the hygiene endpoint without caching and forwards cancellation', async () => {
    const controller = new AbortController();
    const api = vi.fn(async (_path: string, _init?: RequestInit) => new Response(JSON.stringify(response()), { status: 200 }));

    await fetchLicenseHygiene(api, controller.signal);

    expect(api).toHaveBeenCalledOnce();
    expect(api.mock.calls[0][0]).toBe('/api/licenses/hygiene');
    expect(api.mock.calls[0][1]).toMatchObject({ cache: 'no-store', signal: controller.signal });
  });

  it('shows explainable evidence, friendly and original identifiers, and source-specific freshness', async () => {
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => response()} />);

    const account = await screen.findByText('Ada Lovelace');
    const accountRow = account.closest('tr')!;
    expect(accountRow.textContent).toContain('ada@example.com');
    expect(accountRow.textContent).toContain('Disabled');
    expect(accountRow.textContent).toContain('Office 365 E3');
    expect(accountRow.textContent).toContain('ENTERPRISEPACK');
    expect(accountRow.textContent).toContain('sku-e3');
    expect(accountRow.textContent).toContain('Microsoft Graph user assignment');
    expect(screen.getByText(/Records assessed: 3/)).toBeTruthy();
    expect(screen.getByText(/unscanned users.*unknown/i)).toBeTruthy();
    expect(screen.getAllByText(/Inactivity.*not assessed/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/overlap.*not assessed/i).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('status').some(element => element.textContent?.includes('License inventory'))).toBe(true);
    expect(screen.getAllByRole('status').some(element => element.textContent?.includes('User evidence'))).toBe(true);

    const unknownRow = screen.getByText('unknown-sku').closest('tr')!;
    expect(unknownRow.textContent).not.toContain('unknown-sku unknown-sku');
    expect(unknownRow.textContent).toContain('Product metadata unavailable');
  });

  it('recalculates both table results and KPI counts for search and SKU filters', async () => {
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => response()} />);
    await screen.findByText('Ada Lovelace');
    const search = screen.getByRole('searchbox', { name: 'Search license evidence' });
    fireEvent.change(search, { target: { value: 'Ada' } });

    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toContain('1');
    expect(screen.getByText('No matching capacity results.')).toBeTruthy();
    expect(screen.queryByText('Grace Hopper')).toBeNull();

    fireEvent.change(search, { target: { value: '' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'SKU' }), { target: { value: 'sku-e3' } });

    expect(screen.getAllByText('Office 365 E3').length).toBeGreaterThan(0);
    expect(screen.queryByText('Microsoft 365 E5')).toBeNull();
    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toContain('1');
    expect(screen.queryByText('Grace Hopper')).toBeNull();
  });

  it('toggles category tiles and clear filters resets selection and both pages', async () => {
    const many = response({
      capacityItems: Array.from({ length: 26 }, (_, index) => ({
        skuId: `sku-${index}`,
        partNumber: `PART-${index}`,
        displayName: `Plan ${index}`,
        purchased: 10,
        assigned: 4,
        available: 6,
      })),
      disabledAccounts: Array.from({ length: 28 }, (_, index) => ({
        id: `person-${index}`,
        displayName: `Person ${index}`,
        userPrincipalName: `person${index}@example.com`,
        assignedLicenses: [{ skuId: 'sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3' }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph user assignment',
      })),
    });
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => many} />);
    await screen.findByText('Person 0');

    const capacityPagination = screen.getByRole('navigation', { name: 'Capacity pages' });
    expect(capacityPagination.textContent).toContain('Page 1 of 2');
    fireEvent.click(within(capacityPagination).getByRole('button', { name: 'Next page' }));
    expect(capacityPagination.textContent).toContain('Page 2 of 2');

    const disabledTile = document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter') as HTMLButtonElement;
    expect(disabledTile.tagName).toBe('BUTTON');
    fireEvent.click(disabledTile);
    expect(disabledTile.getAttribute('aria-pressed')).toBe('true');
    const accountPagination = screen.getByRole('navigation', { name: 'Disabled-account pages' });
    expect(accountPagination.textContent).toContain('Page 1 of 2');
    fireEvent.click(within(accountPagination).getByRole('button', { name: 'Next page' }));
    expect(await screen.findByText('Person 25')).toBeTruthy();
    fireEvent.click(disabledTile);
    expect(disabledTile.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByText('Every KPI and table recalculates when you filter.')).toBeTruthy();
    expect(screen.getByRole('navigation', { name: 'Capacity pages' }).textContent).toContain('Page 1 of 2');

    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect((screen.getByRole('searchbox', { name: 'Search license evidence' }) as HTMLInputElement).value).toBe('');
    expect((screen.getByRole('combobox', { name: 'SKU' }) as HTMLSelectElement).value).toBe('');
    expect(accountPagination.textContent).toContain('Page 1 of 2');
  });

  it('paginates only after applying base filters', async () => {
    const many = response({
      disabledAccounts: Array.from({ length: 30 }, (_, index) => ({
        id: `account-${index}`,
        displayName: `Account ${index}`,
        userPrincipalName: `account${index}@example.com`,
        assignedLicenses: [{ skuId: 'sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3' }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph',
      })),
    });
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => many} />);
    await screen.findByText('Account 0');
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search license evidence' }), { target: { value: 'Account 2' } });

    expect(screen.getByRole('navigation', { name: 'Disabled-account pages' }).textContent).toContain('Page 1 of 1');
    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toContain('11');
    expect(screen.queryByText('Account 0')).toBeNull();
  });

  it('never presents unavailable or partial assessments as healthy zero', async () => {
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => response({
      inventory: { freshness: 'unavailable', partialData: true, fetchedAt: null, error: { category: 'source_unavailable', message: 'Inventory denied' } },
      userEvidence: { freshness: 'live', partialData: true, fetchedAt: stamp, error: null },
      coverage: { recordsAssessed: 2, completed: false, stopReason: 'page_limit', missingEvidenceRecords: 1 },
      capacityItems: [],
      disabledAccounts: [],
    })} />);

    expect((await screen.findAllByText(/Inventory denied/)).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/partial/i).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/unavailable/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Records assessed: 2/)).toBeTruthy();
    expect(screen.getByText(/1 record.*missing.*evidence/i)).toBeTruthy();
    expect(screen.queryByText('0 disabled accounts')).toBeNull();
    expect(screen.queryByText('No disabled accounts found')).toBeNull();
    expect(screen.getByText(/No capacity assessment is available/)).toBeTruthy();
    expect(document.querySelector('.license-hygiene-kpi--capacity .metric-card--filter')?.textContent).toContain('Unavailable');
    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toContain('0 observed');
    expect(document.querySelector('.license-hygiene-kpi--accounts')?.classList.contains('is-incomplete')).toBe(true);
    expect(screen.getByText(/Coverage totals describe the full observed scan/)).toBeTruthy();
  });

  it('keeps filter-empty account copy scoped to matching findings without hiding partial coverage', async () => {
    const partial = response({
      userEvidence: { freshness: 'live', partialData: true, fetchedAt: stamp, error: null },
      coverage: { recordsAssessed: 2, completed: false, stopReason: 'page_limit', missingEvidenceRecords: 0 },
    });
    const { rerender } = render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => partial} />);
    await screen.findByText('Ada Lovelace');
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search license evidence' }), { target: { value: 'not-observed' } });

    expect(screen.getByText('No matching disabled-account findings in the observed partial scan.')).toBeTruthy();
    expect(screen.getByText(/Records assessed: 2/)).toBeTruthy();
    expect(screen.getAllByText(/Partial scan/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Coverage totals describe the full observed scan/)).toBeTruthy();

    rerender(<LicenseHygienePage
      workspaceId="workspace-1"
      loadHygiene={async () => response()}
    />);
    await screen.findByText('Ada Lovelace');
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search license evidence' }), { target: { value: 'not-observed' } });
    expect(screen.getByText('No disabled accounts match the current filters.')).toBeTruthy();
  });

  it('keeps verified account evidence visible when the inventory source fails', async () => {
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => response({
      inventory: { freshness: 'unavailable', partialData: true, fetchedAt: null, error: { category: 'permission_denied', message: 'Inventory source denied' } },
      capacityItems: [],
    })} />);

    expect(await screen.findByText('Ada Lovelace')).toBeTruthy();
    expect(screen.getByText('No capacity assessment is available for this snapshot.')).toBeTruthy();
    expect(screen.getAllByText(/Inventory source denied/).length).toBeGreaterThan(0);
  });

  it('shows a permission state when the API final authorization gate returns 403', async () => {
    const loader = async () => {
      throw Object.assign(new Error('forbidden'), { status: 403 });
    };
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={loader} />);

    expect(await screen.findByText('You do not have permission to view license hygiene.')).toBeTruthy();
    expect(screen.queryByText('License hygiene data is unavailable. Try again later.')).toBeNull();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });

  it('cancels the previous workspace request and hides its rows during the replacement load', async () => {
    let resolveNew!: (value: LicenseHygieneResponse) => void;
    const oldLoader = vi.fn(async (_signal?: AbortSignal) => response());
    const newLoader = vi.fn((_signal?: AbortSignal) => new Promise<LicenseHygieneResponse>(resolve => { resolveNew = resolve; }));
    const { rerender } = render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={oldLoader} />);
    expect(await screen.findByText('Ada Lovelace')).toBeTruthy();
    fireEvent.change(screen.getByRole('searchbox', { name: 'Search license evidence' }), { target: { value: 'Ada' } });
    fireEvent.change(screen.getByRole('combobox', { name: 'SKU' }), { target: { value: 'sku-e3' } });
    fireEvent.click(document.querySelector('.license-hygiene-kpi--capacity .metric-card--filter')!);

    rerender(<LicenseHygienePage workspaceId="workspace-2" loadHygiene={newLoader} />);

    expect(screen.queryByText('Ada Lovelace')).toBeNull();
    expect(screen.getByRole('status').textContent).toContain('Loading license hygiene');
    expect(screen.getByLabelText('License hygiene results').getAttribute('aria-busy')).toBe('true');
    expect(oldLoader.mock.calls[0][0]?.aborted).toBe(true);
    await waitFor(() => expect(newLoader).toHaveBeenCalledOnce());
    expect(newLoader.mock.calls[0][0]?.aborted).toBe(false);
    resolveNew(response({ disabledAccounts: [] }));
    await waitFor(() => expect(screen.queryByText('Ada Lovelace')).toBeNull());
    expect((screen.getByRole('searchbox', { name: 'Search license evidence' }) as HTMLInputElement).value).toBe('');
    expect((screen.getByRole('combobox', { name: 'SKU' }) as HTMLSelectElement).value).toBe('');
    expect(document.querySelector('.license-hygiene-kpi--capacity .metric-card--filter')?.getAttribute('aria-pressed')).toBe('false');
  });

  it('links to user details only when both module grants and the capability are available', async () => {
    const { rerender } = render(<LicenseHygienePage
      workspaceId="workspace-1"
      loadHygiene={async () => response()}
      capabilities={[{ capability: 'users.view', state: 'read_only', reasonCode: 'role_read_only' }]}
      enabledModules={['users', 'license-hygiene']}
      assignedModules={['users', 'license-hygiene']}
    />);
    expect((await screen.findByRole('link', { name: 'Ada Lovelace' })).getAttribute('href')).toBe('/users/user-ada');

    rerender(<LicenseHygienePage
      workspaceId="workspace-1"
      loadHygiene={async () => response()}
      capabilities={[{ capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }]}
      enabledModules={['license-hygiene']}
      assignedModules={['users', 'license-hygiene']}
    />);
    expect(await screen.findByText('Ada Lovelace')).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();

    rerender(<LicenseHygienePage
      workspaceId="workspace-1"
      loadHygiene={async () => response()}
      capabilities={[{ capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }]}
      enabledModules={['users', 'license-hygiene']}
      assignedModules={['license-hygiene']}
    />);
    expect(await screen.findByText('Ada Lovelace')).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();

    rerender(<LicenseHygienePage
      workspaceId="workspace-1"
      loadHygiene={async () => response()}
      capabilities={[{ capability: 'users.view', state: 'hidden', reasonCode: 'role_required' }]}
      enabledModules={['users', 'license-hygiene']}
      assignedModules={['users', 'license-hygiene']}
    />);
    expect(await screen.findByText('Ada Lovelace')).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Ada Lovelace' })).toBeNull();
  });

  it('keeps category and search filters keyboard reachable with native controls', async () => {
    render(<LicenseHygienePage workspaceId="workspace-1" loadHygiene={async () => response()} />);
    await screen.findByText('Ada Lovelace');
    const category = document.querySelector('.license-hygiene-kpi--capacity .metric-card--filter') as HTMLButtonElement;
    category.focus();

    expect(document.activeElement).toBe(category);
    expect(category.getAttribute('type')).toBe('button');
    expect(category.getAttribute('tabindex')).not.toBe('-1');
    expect(screen.getByRole('searchbox', { name: 'Search license evidence' }).tagName).toBe('INPUT');
    expect(screen.getByRole('combobox', { name: 'SKU' }).tagName).toBe('SELECT');
  });
});
