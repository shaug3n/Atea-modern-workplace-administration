import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceConfigurationTab } from '../../../../src/Web/src/features/devices/DeviceConfigurationTab';
import type { Device360Response, DeviceConfigurationAssignmentTarget, DeviceConfigurationState } from '../../../../src/Web/src/features/devices/device360Api';

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

const configuration = (id: string, state: string): DeviceConfigurationState => ({
  id,
  displayName: `Policy ${id}`,
  state,
  platformType: 'windows10',
  settingCount: 3,
  version: 1,
});

describe('DeviceConfigurationTab', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it('reported_configuration_is_the_default_view', async () => {
    apiMock.mockResolvedValue(response([]));
    render(<DeviceConfigurationTab managedDeviceId="managed-1" active />);

    expect(await screen.findByRole('heading', { name: 'Reported by device' })).toBeTruthy();
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual(['/api/devices/managed-1/configuration/reported']);
    expect(screen.getByRole('button', { name: 'Reported by device' }).getAttribute('aria-pressed')).toBe('true');
  });

  it('configuration_state_counts_cover_all_loaded_records', async () => {
    const rows = [
      ...Array.from({ length: 11 }, (_, index) => configuration(`compliant-${index}`, 'compliant')),
      configuration('unknown-1', 'futureState'),
    ];
    apiMock.mockResolvedValue(response(rows));
    render(<DeviceConfigurationTab managedDeviceId="managed-1" active />);

    const compliant = await screen.findByRole('button', { name: 'Compliant 11' });
    expect(compliant.getAttribute('aria-pressed')).toBe('false');
    expect(screen.getByRole('button', { name: 'Future State 1' })).toBeTruthy();
    expect(screen.queryByText('Policy unknown-1')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));
    expect(await screen.findByText('Policy unknown-1')).toBeTruthy();
    expect(screen.getByText('futureState').closest('[data-tone="neutral"]')).toBeTruthy();
  });

  it('unknown_states_remain_filterable_and_not_success', async () => {
    apiMock.mockResolvedValue(response([configuration('future-1', 'futureState')]));
    render(<DeviceConfigurationTab managedDeviceId="managed-1" active />);

    const filter = await screen.findByRole('button', { name: 'Future State 1' });
    fireEvent.click(filter);
    expect(filter.getAttribute('aria-pressed')).toBe('true');
    expect(screen.getByText('futureState').closest('[data-tone="neutral"]')).toBeTruthy();
    expect(document.querySelector('[data-tone="success"]')).toBeNull();
  });

  it('assignment_view_is_labeled_and_bounded', async () => {
    const assignments: DeviceConfigurationAssignmentTarget[] = [{
      configurationId: 'policy-1',
      configurationName: 'Wi-Fi policy',
      assignmentId: 'assignment-1',
      assignmentKind: 'exclude',
      targetType: 'group',
      groupId: 'group-1',
      filterId: 'filter-1',
      filterType: 'include',
    }];
    apiMock.mockImplementation(async (path: string) => path.endsWith('/configuration/reported')
      ? response([configuration('policy-1', 'compliant')])
      : response(assignments));
    render(<DeviceConfigurationTab managedDeviceId="managed-1" active />);

    await screen.findByText('Policy policy-1');
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual(['/api/devices/managed-1/configuration/reported']);
    fireEvent.click(screen.getByRole('button', { name: 'From assignment' }));

    expect(await screen.findByText('Exclude')).toBeTruthy();
    expect(screen.getByText(/Group ID hidden/)).toBeTruthy();
    expect(screen.getByText('Include · ID hidden')).toBeTruthy();
    expect(await screen.findByText(/not complete Settings Catalog coverage/i)).toBeTruthy();
    expect(screen.getByText(/metadata do not prove that a policy applies to this device/i)).toBeTruthy();
    expect(document.body.textContent).not.toMatch(/is currently effective/i);
    fireEvent.click(screen.getByRole('button', { name: 'Show technical IDs' }));
    expect(screen.getByText('Include · filter-1')).toBeTruthy();
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/devices/managed-1/configuration/reported',
      '/api/devices/managed-1/configuration/assignments',
    ]);
  });

  it('empty_reported_list_does_not_claim_no_assignments', async () => {
    apiMock.mockResolvedValue(response([], { status: 'no_reported_policies' }));
    render(<DeviceConfigurationTab managedDeviceId="managed-1" active />);

    expect(await screen.findByText(/absence of reported policies does not confirm whether policies are assigned/i)).toBeTruthy();
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/^No policies are assigned\.$/i)).toBeNull();
  });

  it('device_change_never_keeps_reported_rows_from_the_previous_managed_device', async () => {
    apiMock.mockImplementation(async (path: string) => path.includes('/managed-a/')
      ? response([configuration('a-policy', 'compliant')])
      : response([configuration('b-policy', 'noncompliant')]));
    const view = render(<DeviceConfigurationTab managedDeviceId="managed-a" active />);
    expect(await screen.findByText('Policy a-policy')).toBeTruthy();

    view.rerender(<DeviceConfigurationTab managedDeviceId="managed-b" active />);

    expect(screen.queryByText('Policy a-policy')).toBeNull();
    expect(await screen.findByText('Policy b-policy')).toBeTruthy();
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/devices/managed-a/configuration/reported',
      '/api/devices/managed-b/configuration/reported',
    ]);
  });
});
