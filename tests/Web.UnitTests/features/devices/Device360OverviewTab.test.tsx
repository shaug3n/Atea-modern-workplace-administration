import React from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ApiFetch, ManagedDevice } from '../../../../src/Web/src/features/devices/devicesApi';
import {
  fetchDeviceApps,
  fetchDeviceCompliancePolicies,
  fetchDeviceConfigurationAssignments,
  fetchDeviceProtection,
  fetchDeviceReportedConfiguration,
} from '../../../../src/Web/src/features/devices/device360Api';
import { Device360OverviewTab } from '../../../../src/Web/src/features/devices/Device360OverviewTab';

const device: ManagedDevice = {
  id: 'managed-device-360',
  deviceName: 'WIN-360',
  operatingSystem: 'Windows',
  osVersion: '11',
  complianceState: 'compliant',
  managementState: 'managed',
  managedDeviceOwnerType: 'company',
  userId: 'user-360',
  userDisplayName: 'Alex Example',
  userPrincipalName: 'alex@work.example',
  lastSyncDateTime: '2026-10-08T10:00:00Z',
  isEncrypted: true,
  manufacturer: 'Contoso',
  model: 'Example 1',
  serialNumber: 'serial-360',
};

const policies = {
  status: 'succeeded' as const,
  data: [{ id: 'policy-1', displayName: 'Reported policy', state: 'noncompliant', platformType: 'windows', settingCount: 4, version: 3 }],
  retrievedAt: '2026-10-08T10:05:00Z',
  partialData: false,
  error: null,
  retryAfterSeconds: null,
  graphCorrelationId: null,
  graphRequestId: null,
};

describe('Device360OverviewTab', () => {
  afterEach(() => { cleanup(); Reflect.deleteProperty(navigator, 'clipboard'); });

  it('overview_explains_overall_vs_per_policy_compliance', () => {
    render(<Device360OverviewTab device={device} policies={policies} />);
    expect(screen.getByText('Compliant')).toBeTruthy();
    expect(screen.getByText('Reported policy')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'How overall and policy compliance differ' }));
    expect(screen.getByRole('dialog').textContent).toMatch(/overall Intune compliance and individual policy reports can disagree/i);
    expect(screen.getByRole('dialog').textContent).toMatch(/different times/i);
  });

  it('shows an unnamed placeholder and preserves nullable encryption values', () => {
    render(<Device360OverviewTab
      device={{ ...device, deviceName: null, isEncrypted: true }}
      policies={policies}
    />);
    expect(screen.getByText('Unnamed device')).toBeTruthy();
    expect(screen.getByText('Reported encryption').parentElement?.textContent).toContain('Yes');

    cleanup();
    render(<Device360OverviewTab
      device={{ ...device, deviceName: null, isEncrypted: false }}
      policies={policies}
    />);
    expect(screen.getByText('Reported encryption').parentElement?.textContent).toContain('No');

    cleanup();
    render(<Device360OverviewTab
      device={{ ...device, deviceName: null, isEncrypted: null }}
      policies={policies}
    />);
    expect(screen.getByText('Reported encryption').parentElement?.textContent).toContain('Unknown');
  });

  it('shows provenance for the five reported device attributes and source timestamps separately', () => {
    render(<Device360OverviewTab device={device} policies={policies} />);
    const sources = Array.from(document.querySelectorAll('.provenance-chip__source')).map(item => item.textContent);
    expect(sources).toEqual(expect.arrayContaining([
      'managedDevice.deviceName',
      'managedDevice.managedDeviceOwnerType',
      'managedDevice.userDisplayName',
      'managedDevice.userPrincipalName',
      'managedDevice.lastSyncDateTime',
    ]));
    expect(screen.getByText(/Last check-in/).closest('.device360-fact')?.textContent).toContain('2026');
    expect(screen.getByText(/Policies retrieved/).textContent).toContain('2026');
  });

  it('portal_link_is_best_effort_with_copyable_managed_device_id', () => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn() } });
    render(<Device360OverviewTab device={device} policies={policies} />);
    const portal = screen.getByRole('link', { name: 'Open Intune portal' });
    expect(portal.getAttribute('href')).toBe('https://intune.microsoft.com/');
    expect(portal.getAttribute('href')).not.toContain(device.id);
    fireEvent.click(screen.getByText('Technical details'));
    expect(screen.getByText(device.id)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Copy Managed device ID' })).toBeTruthy();
  });
});

describe('Device 360 API helpers', () => {
  const helpers = [
    [fetchDeviceCompliancePolicies, '/compliance-policies'],
    [fetchDeviceReportedConfiguration, '/configuration/reported'],
    [fetchDeviceConfigurationAssignments, '/configuration/assignments'],
    [fetchDeviceApps, '/apps'],
    [fetchDeviceProtection, '/protection'],
  ] as const;

  it('device360_helpers_use_same_origin_no_store_routes', async () => {
    for (const [helper, suffix] of helpers) {
      const api = vi.fn<ApiFetch>().mockResolvedValue(new Response(JSON.stringify({ status: 'succeeded', data: [] }), { status: 200 }));
      await helper(api, 'managed/device 360');
      expect(api).toHaveBeenCalledWith(`/api/devices/managed%2Fdevice%20360${suffix}`, { cache: 'no-store' });
    }
  });

  it('retains_section_error_bodies_and_does_not_turn_invalid_json_into_empty_success', async () => {
    const denied = vi.fn<ApiFetch>().mockResolvedValue(new Response(JSON.stringify({
      status: 'graph_forbidden',
      data: null,
      retrievedAt: '2026-10-08T10:00:00Z',
      partialData: false,
      error: { category: 'authorization', message: 'Denied', state: 'graph_forbidden', statusCode: 403, retryAfterSeconds: null },
      retryAfterSeconds: null,
      graphCorrelationId: 'correlation-1',
      graphRequestId: 'request-1',
    }), { status: 403 }));
    const deniedResult = await fetchDeviceCompliancePolicies(denied, 'managed-device-360');
    expect(deniedResult.status).toBe('graph_forbidden');
    expect(deniedResult.error?.message).toBe('Denied');
    expect(deniedResult.graphCorrelationId).toBe('correlation-1');

    const malformed = vi.fn<ApiFetch>().mockResolvedValue(new Response('not json', { status: 200 }));
    const malformedResult = await fetchDeviceCompliancePolicies(malformed, 'managed-device-360');
    expect(malformedResult.status).toBe('failed');
    expect(malformedResult.data).toBeNull();
  });
});
