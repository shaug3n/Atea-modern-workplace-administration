import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';
import type { LicenseHygieneResponse } from '../../src/Web/src/features/licenses/hygiene/LicenseHygienePage';

const apiFetch = vi.hoisted(() => vi.fn());
vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));

const workspaceId = 'fixture-workspace';
const stamp = '2026-10-08T10:00:00Z';
const session = {
  user: { displayName: 'Fixture administrator' },
  workspace: {
    id: workspaceId,
    name: 'Fixture workspace',
    enabledModules: ['licenses', 'license-hygiene', 'users'],
    moduleAccess: ['licenses', 'license-hygiene', 'users'],
  },
};
const capabilities = {
  workspaceId,
  evaluatedAt: stamp,
  sourceState: 'graph_authoritative',
  capabilities: [
    { capability: 'licenses.view', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'licenses.hygiene.view', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
  ],
};

function hygieneResponse(overrides: Partial<LicenseHygieneResponse> = {}): LicenseHygieneResponse {
  return {
    access: { state: 'allowed' },
    inventory: { freshness: 'live', partialData: false, fetchedAt: stamp, error: null },
    userEvidence: { freshness: 'live', partialData: false, fetchedAt: stamp, error: null },
    coverage: { recordsAssessed: 3, completed: true, stopReason: 'completed', missingEvidenceRecords: 1 },
    capacityItems: [
      { skuId: 'fixture-sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 20, assigned: 15, available: 5 },
      { skuId: 'fixture-sku-e5', partNumber: 'SPE_E5', displayName: 'Microsoft 365 E5', purchased: 10, assigned: 8, available: 2 },
    ],
    disabledAccounts: [
      {
        id: 'fixture-user-ada',
        displayName: 'Ada Example',
        userPrincipalName: 'ada@example.test',
        assignedLicenses: [{ skuId: 'fixture-sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3' }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph user assignment',
      },
      {
        id: 'fixture-user-grace',
        displayName: 'Grace Example',
        userPrincipalName: 'grace@example.test',
        assignedLicenses: [{ skuId: 'fixture-sku-e5', partNumber: 'SPE_E5', displayName: 'Microsoft 365 E5' }],
        evidenceAt: stamp,
        evidenceSource: 'Microsoft Graph user assignment',
      },
    ],
    ...overrides,
  };
}

function capabilitySnapshot(capabilityState = 'allowed') {
  return {
    ...capabilities,
    capabilities: capabilities.capabilities.map(decision =>
      decision.capability === 'licenses.hygiene.view' ? { ...decision, state: capabilityState } : decision),
  };
}

function renderApp(path: string, response = hygieneResponse(), snapshot = capabilitySnapshot(), appSession = session) {
  window.history.replaceState({}, '', path);
  apiFetch.mockImplementation(async (requestPath: string, init: RequestInit = {}) => {
    if (requestPath.startsWith('/api/licenses?')) {
      return Response.json({
        items: [{ skuId: 'fixture-sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 20, assigned: 15, available: 5 }],
        total: 1,
        page: 1,
        pageSize: 25,
        fetchedAt: stamp,
        freshness: 'live',
        partialData: false,
        access: { state: 'allowed' },
      });
    }
    if (requestPath === '/api/licenses/hygiene') return Response.json(response);
    throw new Error(`Unexpected fixture request: ${requestPath} ${init.method ?? 'GET'}`);
  });
  return render(<App
    loadSession={async () => appSession}
    loadCapabilities={async () => snapshot}
  />);
}

afterEach(() => {
  cleanup();
  apiFetch.mockReset();
  window.history.replaceState({}, '', '/');
});

describe('License Hygiene owned-page fixtures (Vitest/jsdom; not live-browser E2E)', () => {
  it('navigates from Licenses into the opt-in page and filters a shared evidence snapshot', async () => {
    const requests: Array<{ path: string; method: string }> = [];
    apiFetch.mockImplementation(async (path: string, init: RequestInit = {}) => {
      requests.push({ path, method: init.method ?? 'GET' });
      if (path.startsWith('/api/licenses?')) {
        return Response.json({
          items: [{ skuId: 'fixture-sku-e3', partNumber: 'ENTERPRISEPACK', displayName: 'Office 365 E3', purchased: 20, assigned: 15, available: 5 }],
          total: 1, page: 1, pageSize: 25, fetchedAt: stamp, freshness: 'live', partialData: false, access: { state: 'allowed' },
        });
      }
      if (path === '/api/licenses/hygiene') return Response.json(hygieneResponse());
      throw new Error(`Unexpected fixture request: ${path}`);
    });
    window.history.replaceState({}, '', '/licenses');
    render(<App loadSession={async () => session} loadCapabilities={async () => capabilitySnapshot()} />);

    const hygieneLink = await screen.findByRole('link', { name: 'Review license hygiene' });
    hygieneLink.addEventListener('click', event => {
      event.preventDefault();
      window.history.pushState({}, '', hygieneLink.getAttribute('href'));
      window.dispatchEvent(new PopStateEvent('popstate'));
    }, { once: true });
    fireEvent.click(hygieneLink);
    expect(await screen.findByRole('heading', { name: 'License hygiene', level: 1 })).toBeTruthy();
    expect(await screen.findByText('Ada Example')).toBeTruthy();
    expect(screen.getAllByText('Microsoft Graph user assignment')).toHaveLength(2);
    expect(screen.getByText(/Records assessed: 3/)).toBeTruthy();
    expect(screen.getByText(/1 record had missing or unusable evidence/)).toBeTruthy();

    const disabledAccounts = document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter') as HTMLButtonElement;
    disabledAccounts.focus();
    expect(document.activeElement).toBe(disabledAccounts);
    fireEvent.click(disabledAccounts);
    expect(disabledAccounts.getAttribute('aria-pressed')).toBe('true');
    expect(screen.queryByRole('heading', { name: 'Verified license capacity' })).toBeNull();
    expect(screen.getByRole('heading', { name: 'Disabled-account review' })).toBeTruthy();

    fireEvent.change(screen.getByRole('combobox', { name: 'SKU' }), { target: { value: 'fixture-sku-e5' } });
    expect(screen.getByText('Grace Example')).toBeTruthy();
    expect(screen.queryByText('Ada Example')).toBeNull();
    expect(requests.some(request => request.path === '/api/licenses/hygiene' && request.method === 'GET')).toBe(true);
    expect(requests.every(request => request.path.startsWith('/api/') && request.method === 'GET')).toBe(true);
  });

  it('hides the entry and denies direct routes when capability or explicit module assignment is missing', async () => {
    const { unmount } = renderApp('/licenses', hygieneResponse(), capabilitySnapshot('hidden'));
    expect(await screen.findByRole('heading', { name: 'License inventory' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Review license hygiene' })).toBeNull();
    unmount();

    renderApp('/licenses/hygiene', hygieneResponse(), capabilitySnapshot('hidden'));
    expect(await screen.findByText(/Data cannot be shown right now/)).toBeTruthy();
    expect(apiFetch.mock.calls.some(([path]) => path === '/api/licenses/hygiene')).toBe(false);
    cleanup();
    apiFetch.mockReset();

    renderApp('/licenses/hygiene', hygieneResponse(), capabilitySnapshot(), {
      ...session,
      workspace: { ...session.workspace, moduleAccess: ['licenses', 'users'] },
    });
    expect(await screen.findByText(/You don't have access to License hygiene/)).toBeTruthy();
    expect(apiFetch.mock.calls.some(([path]) => path === '/api/licenses/hygiene')).toBe(false);
  });

  it('distinguishes a completed empty result from an incomplete scan with zero observed findings', async () => {
    renderApp('/licenses/hygiene', hygieneResponse({ disabledAccounts: [] }));
    expect(await screen.findByText('No disabled accounts match the current filters.')).toBeTruthy();
    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toMatch(/0/);
    cleanup();
    apiFetch.mockReset();

    const incomplete = hygieneResponse({
      userEvidence: { freshness: 'live', partialData: true, fetchedAt: stamp, error: null },
      coverage: { recordsAssessed: 3, completed: false, stopReason: 'page_limit', missingEvidenceRecords: 1 },
      disabledAccounts: [],
    });
    renderApp('/licenses/hygiene', incomplete);
    expect(await screen.findByText('No matching disabled-account findings in the observed partial scan.')).toBeTruthy();
    expect(screen.getByText(/Records assessed: 3.*Partial scan/i)).toBeTruthy();
    expect(document.querySelector('.license-hygiene-kpi--accounts .metric-card--filter')?.textContent).toContain('0 observed');
    expect(screen.getByText(/Unscanned users.*unknown/i)).toBeTruthy();
  });

  it('keeps responsive table labels and keyboard-operated evidence help available in a narrow viewport', async () => {
    const originalWidth = window.innerWidth;
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 375 });
    try {
      renderApp('/licenses/hygiene');
      expect(await screen.findByText('Ada Example')).toBeTruthy();
      const row = screen.getByText('Ada Example').closest('tr')!;
      expect(within(row).getByText('ada@example.test').closest('[data-label]')?.getAttribute('data-label')).toBe('Account');
      expect([...row.querySelectorAll('td')].every(cell => Boolean(cell.getAttribute('data-label')))).toBe(true);

      const help = screen.getByRole('button', { name: 'Disabled accounts' });
      help.focus();
      fireEvent.keyDown(help, { key: ' ' });
      expect(await screen.findByRole('dialog', { name: 'Disabled accounts' })).toBeTruthy();
      fireEvent.keyDown(help, { key: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Disabled accounts' })).toBeNull());
      expect(document.activeElement).toBe(help);
    } finally {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: originalWidth });
    }
  });
});
