import React from 'react';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CapabilityDecision } from '../../src/Web/src/capabilities/capabilityTypes';
import { DeviceDetailPage } from '../../src/Web/src/features/devices/DeviceDetailPage';
import { DevicesPage } from '../../src/Web/src/features/devices/DevicesPage';
import type { DeviceFilters, DevicesResponse, ManagedDevice } from '../../src/Web/src/features/devices/devicesApi';

const device360Styles = readFileSync(resolve(process.cwd(), '../../src/Web/src/features/devices/device360.css'), 'utf8');
const defaultViewportWidth = window.innerWidth;
const apiFetch = vi.hoisted(() => vi.fn());
const issueReporter = vi.hoisted(() => ({ report: vi.fn(), clear: vi.fn() }));

vi.mock('../../src/Web/src/auth/useApi', () => ({ useApi: () => apiFetch }));
vi.mock('../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => issueReporter }));

const managedDeviceId = 'managed/device #1';
const encodedDeviceId = encodeURIComponent(managedDeviceId);
const device: ManagedDevice = {
  id: managedDeviceId,
  deviceName: 'WIN-DEVICE-01',
  operatingSystem: 'Windows',
  complianceState: 'compliant',
  userDisplayName: 'Ada Lovelace',
  userPrincipalName: 'ada@example.com',
};
const capabilities: CapabilityDecision[] = [
  { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
  { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' },
];

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function sectionResponse<T>(status: string, data: T | null, partialData = false) {
  return {
    status,
    data,
    retrievedAt: '2026-10-09T09:00:00Z',
    partialData,
    error: status === 'succeeded' || status === 'partial' ? null : {
      category: status === 'graph_forbidden' ? 'authorization' : 'unavailable',
      message: status,
      state: status,
      statusCode: status === 'graph_forbidden' ? 403 : 503,
      retryAfterSeconds: null,
    },
    retryAfterSeconds: null,
    graphCorrelationId: null,
    graphRequestId: null,
  };
}

function deviceResponse() {
  return jsonResponse(device);
}

function configurationFixture() {
  return sectionResponse('succeeded', [{
    id: 'policy-1',
    displayName: 'Baseline configuration',
    state: 'compliant',
    platformType: 'windows10',
    settingCount: 4,
    version: 3,
  }]);
}

const emptyPolicies = sectionResponse('succeeded', []);
const devicesResult = (items: ManagedDevice[]): DevicesResponse => ({
  items,
  total: items.length,
  fetchedAt: '2026-10-09T09:00:00Z',
  freshness: 'live',
  partialData: false,
  access: { state: 'allowed' },
});

afterEach(() => {
  cleanup();
  document.querySelector('[data-device360-test-styles]')?.remove();
  vi.clearAllMocks();
  window.history.replaceState({}, '', '/');
  window.innerWidth = defaultViewportWidth;
});

describe('Devices owned-page journeys (DOM-backed mocked API)', () => {
  beforeEach(() => {
    const style = document.createElement('style');
    style.dataset.device360TestStyles = '';
    style.textContent = device360Styles;
    document.head.append(style);
  });

  it('directory_compliance_tile_filters_and_clears', async () => {
    const requests: DeviceFilters[] = [];
    const loadDevices = vi.fn(async (filters: DeviceFilters) => {
      requests.push(filters);
      const all = [
        { ...device, id: 'device-compliant', deviceName: 'WIN-COMPLIANT', complianceState: 'compliant' },
        { ...device, id: 'device-noncompliant', deviceName: 'WIN-NONCOMPLIANT', complianceState: 'noncompliant' },
      ];
      const items = filters.complianceState ? all.filter(item => item.complianceState === filters.complianceState) : all;
      return devicesResult(items);
    });

    render(<DevicesPage loadDevices={loadDevices} capabilities={capabilities} />);
    expect(await screen.findByRole('link', { name: 'WIN-COMPLIANT' })).toBeTruthy();
    const compliantTile = screen.getByRole('button', { name: /Compliant/ });
    const complianceFilter = screen.getByLabelText('Compliance');

    fireEvent.click(compliantTile);
    await waitFor(() => expect(requests.at(-1)?.complianceState).toBe('compliant'));
    await waitFor(() => expect(screen.getByRole('button', { name: /Compliant/ }).getAttribute('aria-pressed')).toBe('true'));
    expect((complianceFilter as HTMLSelectElement).value).toBe('compliant');
    expect(await screen.findByRole('link', { name: 'WIN-COMPLIANT' })).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'WIN-NONCOMPLIANT' })).toBeNull();

    fireEvent.change(screen.getByLabelText('Operating system'), { target: { value: 'Windows' } });
    await waitFor(() => expect(requests.at(-1)).toMatchObject({ complianceState: 'compliant', operatingSystem: 'Windows' }));
    expect((screen.getByLabelText('Compliance') as HTMLSelectElement).value).toBe('compliant');

    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    await waitFor(() => expect(requests.at(-1)).toEqual({ search: '', complianceState: '', operatingSystem: '' }));
    expect(compliantTile.getAttribute('aria-pressed')).toBe('false');
    expect((screen.getByLabelText('Compliance') as HTMLSelectElement).value).toBe('');
    expect(await screen.findByRole('link', { name: 'WIN-NONCOMPLIANT' })).toBeTruthy();
  });

  it('device_360_uses_direct_route_and_lazy_section_requests', async () => {
    window.history.replaceState({}, '', `/devices/${encodedDeviceId}`);
    const requests: Array<{ path: string; method: string }> = [];
    apiFetch.mockImplementation(async (path: string, init?: RequestInit) => {
      requests.push({ path, method: init?.method ?? 'GET' });
      expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
      if (path === `/api/devices/${encodedDeviceId}`) return deviceResponse();
      if (path === `/api/devices/${encodedDeviceId}/compliance-policies`) return jsonResponse(emptyPolicies);
      if (path === `/api/devices/${encodedDeviceId}/configuration/reported`) return jsonResponse(configurationFixture());
      if (path === `/api/devices/${encodedDeviceId}/configuration/assignments`) return jsonResponse(sectionResponse('succeeded', [{
        configurationId: 'policy-1',
        configurationName: 'Baseline configuration',
        assignmentId: 'assignment-1',
        assignmentKind: 'include',
        targetType: 'group',
        groupId: 'group-1',
        filterId: null,
        filterType: null,
      }]));
      if (path === `/api/devices/${encodedDeviceId}/apps`) return jsonResponse(sectionResponse('succeeded', []));
      if (path === `/api/devices/${encodedDeviceId}/protection`) return jsonResponse(sectionResponse('succeeded', {
        antiMalwareVersion: null,
        controlledConfigurationEnabled: null,
        deviceState: null,
        engineVersion: null,
        fullScanOverdue: null,
        fullScanRequired: null,
        isVirtualMachine: null,
        lastFullScanDateTime: null,
        lastFullScanSignatureVersion: null,
        lastQuickScanDateTime: null,
        lastQuickScanSignatureVersion: null,
        lastReportedDateTime: null,
        malwareProtectionEnabled: null,
        networkInspectionSystemEnabled: null,
        productStatus: null,
        quickScanOverdue: null,
        realTimeProtectionEnabled: null,
        rebootRequired: null,
        signatureUpdateOverdue: null,
        signatureVersion: null,
        tamperProtectionEnabled: null,
      }));
      return jsonResponse({ error: `Unexpected mocked API request: ${path}` }, 404);
    });

    render(<DeviceDetailPage capabilities={capabilities} />);
    expect(await screen.findByRole('heading', { name: 'WIN-DEVICE-01' })).toBeTruthy();
    await waitFor(() => expect(requests.map(request => request.path)).toEqual([
      `/api/devices/${encodedDeviceId}`,
      `/api/devices/${encodedDeviceId}/compliance-policies`,
    ]));
    expect(requests.some(request => request.path === '/api/devices' || request.path.startsWith('/api/devices?'))).toBe(false);

    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(await screen.findByText('Baseline configuration')).toBeTruthy();
    await waitFor(() => expect(requests.at(-1)?.path).toBe(`/api/devices/${encodedDeviceId}/configuration/reported`));

    fireEvent.click(screen.getByRole('button', { name: 'From assignment' }));
    expect(await screen.findByRole('region', { name: 'Configuration assignment targets' })).toBeTruthy();
    await waitFor(() => expect(requests.at(-1)?.path).toBe(`/api/devices/${encodedDeviceId}/configuration/assignments`));

    fireEvent.click(screen.getByRole('tab', { name: 'Apps' }));
    expect(await screen.findByRole('heading', { name: 'Discovered apps' })).toBeTruthy();
    await waitFor(() => expect(requests.at(-1)?.path).toBe(`/api/devices/${encodedDeviceId}/apps`));

    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    expect(await screen.findByRole('heading', { name: 'Device-reported encryption' })).toBeTruthy();
    await waitFor(() => expect(requests.at(-1)?.path).toBe(`/api/devices/${encodedDeviceId}/protection`));

    const beforeActions = requests.length;
    fireEvent.click(screen.getByRole('tab', { name: 'Actions' }));
    expect(await screen.findByText(/Acceptance does not confirm completion/i)).toBeTruthy();
    expect(requests).toHaveLength(beforeActions);
    expect(requests.some(request => /\/recovery\/|\/actions\//i.test(request.path) || request.method !== 'GET')).toBe(false);
    expect(requests.every(request => new URL(request.path, window.location.origin).origin === window.location.origin)).toBe(true);
  });

  it('tabs_support_keyboard_and_narrow_layout', async () => {
    window.innerWidth = 360;
    window.history.replaceState({}, '', '/devices/device-1');
    apiFetch.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? jsonResponse({ ...device, id: 'device-1' })
      : jsonResponse(emptyPolicies));
    render(<DeviceDetailPage />);

    expect(await screen.findByRole('heading', { name: 'WIN-DEVICE-01' })).toBeTruthy();
    const tablist = screen.getByRole('tablist', { name: 'Device sections' });
    expect(tablist.className).toContain('device360-tabs');
    expect(within(tablist).getAllByRole('tab')).toHaveLength(5);
    const provenance = document.querySelector('.device360-fact .provenance-chip');
    expect(provenance).not.toBeNull();
    expect(getComputedStyle(provenance!).maxWidth).toBe('100%');
    expect(getComputedStyle(provenance!).overflowWrap).toBe('anywhere');
    const overview = screen.getByRole('tab', { name: 'Overview' });
    fireEvent.keyDown(overview, { key: 'ArrowRight' });
    const configuration = screen.getByRole('tab', { name: 'Configuration' });
    expect(configuration.getAttribute('aria-selected')).toBe('true');
    expect(document.activeElement).toBe(configuration);
    expect(screen.getByRole('tabpanel', { name: 'Configuration' })).toBeTruthy();
    expect(tablist.scrollWidth === 0 || tablist.scrollWidth >= tablist.clientWidth).toBe(true);
  });

  it('denied_and_partial_tab_states_remain_explicit', async () => {
    window.history.replaceState({}, '', '/devices/device-1');
    const pending = new Map<string, (response: Response) => void>();
    apiFetch.mockImplementation((path: string) => {
      if (path === '/api/devices/device-1') return Promise.resolve(jsonResponse({ ...device, id: 'device-1' }));
      if (path.endsWith('/compliance-policies')) return Promise.resolve(jsonResponse(sectionResponse('graph_forbidden', null)));
      return new Promise<Response>(resolve => pending.set(path, resolve));
    });
    render(<DeviceDetailPage />);

    expect(await screen.findByRole('heading', { name: 'WIN-DEVICE-01' })).toBeTruthy();
    expect(await screen.findByText(/not allowed to read per-policy compliance reports/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(await screen.findByText(/Loading reported configuration/i)).toBeTruthy();
    const reportedPath = '/api/devices/device-1/configuration/reported';
    await act(async () => pending.get(reportedPath)?.(jsonResponse(sectionResponse('partial', [{
      id: 'policy-partial',
      displayName: 'Partial configuration',
      state: 'futureState',
      platformType: 'windows10',
      settingCount: 1,
      version: 1,
    }], true))));
    expect(await screen.findByText(/Reported configuration is partial/i)).toBeTruthy();
    expect(screen.getByText('Partial configuration')).toBeTruthy();
    expect(screen.getAllByText('Future State').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('tab', { name: 'Apps' }));
    expect(await screen.findByText(/Loading discovered app inventory/i)).toBeTruthy();
    const appsPath = '/api/devices/device-1/apps';
    await act(async () => pending.get(appsPath)?.(jsonResponse(sectionResponse('graph_forbidden', null), 403)));
    expect(await screen.findByText(/not allowed to read this detected-app inventory/i)).toBeTruthy();

    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    expect(await screen.findByText(/Loading Windows protection details/i)).toBeTruthy();
    const protectionPath = '/api/devices/device-1/protection';
    await act(async () => pending.get(protectionPath)?.(jsonResponse(sectionResponse('temporarily_unavailable', null), 503)));
    expect(await screen.findByText(/Windows protection details are temporarily unavailable/i)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });
});
