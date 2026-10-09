import React from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceAppsTab, normalizeDetectedAppPublisher } from '../../../../src/Web/src/features/devices/DeviceAppsTab';
import type { Device360Response, DeviceDetectedApp } from '../../../../src/Web/src/features/devices/device360Api';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const response = (data: unknown, overrides: Partial<Device360Response<unknown>> = {}) => ({
  ok: true,
  status: 200,
  json: async () => ({
    status: 'succeeded',
    data,
    retrievedAt: '2026-10-09T08:00:00Z',
    partialData: false,
    error: null,
    retryAfterSeconds: null,
    graphCorrelationId: null,
    graphRequestId: null,
    ...overrides,
  }),
});

const app = (id: string, displayName: string, publisher: string | null): DeviceDetectedApp => ({
  id,
  displayName,
  version: '4.2.1',
  platform: 'windows',
  publisher,
});

describe('DeviceAppsTab', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it('detected_inventory_discloses_source_limits', async () => {
    apiMock.mockResolvedValue(response([
      app('app-1', 'Contoso Editor', 'Contoso Ltd.'),
      app('app-2', 'Internal Agent', 'CN=agent.example, OU=Engineering, O=Contoso, C=US'),
      app('app-3', 'Unknown Tool', null),
    ], { status: 'partial', partialData: true, retrievedAt: '2026-10-09T08:12:00Z' }));
    render(<DeviceAppsTab managedDeviceId="managed-1" active />);

    expect(await screen.findByRole('cell', { name: 'Contoso Editor' })).toBeTruthy();
    const ordinaryPublisherRow = screen.getByRole('row', { name: /Contoso Editor/ });
    expect(ordinaryPublisherRow.textContent).toContain('4.2.1');
    expect(ordinaryPublisherRow.textContent).toContain('windows');
    expect(ordinaryPublisherRow.textContent).toContain('Contoso Ltd.');
    expect(screen.getAllByText('Unavailable')).toHaveLength(2);
    expect(document.body.textContent).not.toContain('CN=agent.example');
    expect(await screen.findByText(/discovered inventory is not complete/i)).toBeTruthy();
    expect(screen.getByText(/not an assignment or successful-install report/i)).toBeTruthy();
    expect(screen.getByText(/source timestamp: not reported by this source/i)).toBeTruthy();
    expect(screen.getByText(/API retrieval time:.*2026/i)).toBeTruthy();
    expect(screen.getByText(/ownership and platform coverage may vary/i)).toBeTruthy();
    expect(screen.getAllByRole('status').some(status => status.textContent?.match(/partial/i))).toBe(true);
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual(['/api/devices/managed-1/apps']);
  });

  it('publisher_normalization_hides_certificate_distinguished_names', () => {
    expect(normalizeDetectedAppPublisher(' Contoso Ltd. ')).toBe('Contoso Ltd.');
    expect(normalizeDetectedAppPublisher('CN=agent.example, OU=Engineering, O=Contoso, C=US')).toBeNull();
    expect(normalizeDetectedAppPublisher('CN=agent.example')).toBeNull();
    expect(normalizeDetectedAppPublisher('O=Contoso, C=US')).toBeNull();
    expect(normalizeDetectedAppPublisher('  ')).toBeNull();
    expect(normalizeDetectedAppPublisher(null)).toBeNull();
  });

  it('device_change_never_keeps_detected_apps_from_the_previous_managed_device', async () => {
    apiMock.mockImplementation(async (path: string) => path.includes('/managed-a/')
      ? response([app('a-app', 'App for A', 'Publisher A')])
      : response([app('b-app', 'App for B', 'Publisher B')]));
    const view = render(<DeviceAppsTab managedDeviceId="managed-a" active />);
    expect(await screen.findByRole('cell', { name: 'App for A' })).toBeTruthy();

    view.rerender(<DeviceAppsTab managedDeviceId="managed-b" active />);

    expect(screen.queryByRole('cell', { name: 'App for A' })).toBeNull();
    expect(await screen.findByRole('cell', { name: 'App for B' })).toBeTruthy();
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/devices/managed-a/apps',
      '/api/devices/managed-b/apps',
    ]);
  });
});
