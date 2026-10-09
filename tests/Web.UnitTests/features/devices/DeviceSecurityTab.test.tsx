import React from 'react';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceSecurityTab } from '../../../../src/Web/src/features/devices/DeviceSecurityTab';
import type { DeviceWindowsProtectionState } from '../../../../src/Web/src/features/devices/device360Api';
import type { ManagedDevice } from '../../../../src/Web/src/features/devices/devicesApi';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({
  useWorkspaceIssueReporter: () => ({ report: vi.fn(), clear: vi.fn() }),
}));

const response = (body: unknown, status = 200) => ({ ok: status < 400, status, json: async () => body });
const protection: DeviceWindowsProtectionState = {
  antiMalwareVersion: '1.2',
  controlledConfigurationEnabled: true,
  deviceState: 'clean',
  engineVersion: '2.3',
  fullScanOverdue: false,
  fullScanRequired: false,
  isVirtualMachine: false,
  lastFullScanDateTime: null,
  lastFullScanSignatureVersion: null,
  lastQuickScanDateTime: null,
  lastQuickScanSignatureVersion: null,
  lastReportedDateTime: '2026-10-08T09:30:00Z',
  malwareProtectionEnabled: true,
  networkInspectionSystemEnabled: true,
  productStatus: 'active',
  quickScanOverdue: false,
  realTimeProtectionEnabled: true,
  rebootRequired: false,
  signatureUpdateOverdue: false,
  signatureVersion: '4.5',
  tamperProtectionEnabled: true,
};
const section = (data: unknown, status = 'succeeded') => ({
  status, data, retrievedAt: '2026-10-08T10:00:00Z', partialData: false, error: null,
  retryAfterSeconds: null, graphCorrelationId: null, graphRequestId: null,
});
const device = (overrides: Partial<ManagedDevice> = {}): ManagedDevice => ({
  id: 'device-1', deviceName: 'WIN-01', operatingSystem: 'Windows', isEncrypted: null, ...overrides,
});
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

describe('DeviceSecurityTab', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it('nullable_encryption_is_not_false_or_bitlocker_availability', async () => {
    apiMock.mockResolvedValue(response(section(protection)));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    expect(await screen.findByText(/did not report an encryption status/i)).toBeTruthy();
    expect(screen.queryByText(/bitlocker.*not available/i)).toBeNull();

    view.rerender(<DeviceSecurityTab device={device({ isEncrypted: false })} managedDeviceId="device-1" active capabilities={[]} />);
    expect(screen.getByText(/device reports encryption disabled/i)).toBeTruthy();
    view.rerender(<DeviceSecurityTab device={device({ isEncrypted: true })} managedDeviceId="device-1" active capabilities={[]} />);
    expect(screen.getByText(/device reports encryption enabled/i)).toBeTruthy();
    expect(screen.getByText(/does not identify the encryption technology or confirm that a BitLocker recovery key is available/i)).toBeTruthy();
  });

  it('windows_protection_is_lazy_and_keeps_reported_time', async () => {
    apiMock.mockResolvedValue(response(section(protection)));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active={false} capabilities={[]} />);
    expect(apiMock).not.toHaveBeenCalled();
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    expect(await screen.findByText(/Last reported:/)).toBeTruthy();
    expect(document.querySelector('time[datetime="2026-10-08T09:30:00Z"]')).toBeTruthy();
    expect(screen.getByText(/API retrieval time:/).textContent).toContain('API retrieval time');
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/protection', expect.anything());
  });

  it('unsupported_non_windows_is_not_empty_success', async () => {
    const view = render(<DeviceSecurityTab device={device({ operatingSystem: 'iOS', isEncrypted: false })} managedDeviceId="device-1" active capabilities={[]} />);
    expect(await screen.findByText(/not supported for non-Windows devices/i)).toBeTruthy();
    expect(screen.queryByText(/no protection details were reported/i)).toBeNull();
    expect(apiMock).not.toHaveBeenCalled();
    apiMock.mockResolvedValue(response(section(null, 'unsupported'), 501));
    view.rerender(<DeviceSecurityTab device={device({ id: 'device-2', operatingSystem: 'Windows' })} managedDeviceId="device-2" active capabilities={[]} />);
    expect(await screen.findByText(/not supported for this device or source/i)).toBeTruthy();
    expect(screen.queryByText(/not supported for non-Windows devices/i)).toBeNull();
  });

  it('protection_denial_transient_and_unreported_states_stay_distinct', async () => {
    apiMock.mockResolvedValueOnce(response(section(null, 'graph_forbidden'), 403))
      .mockResolvedValueOnce(response(section(null, 'temporarily_unavailable'), 503))
      .mockResolvedValueOnce(response(section(null)));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    expect(await screen.findByText(/not allowed to read Windows protection details/i)).toBeTruthy();
    view.rerender(<DeviceSecurityTab device={device({ id: 'device-2' })} managedDeviceId="device-2" active capabilities={[]} />);
    expect(await screen.findByText(/temporarily unavailable/i)).toBeTruthy();
    expect(screen.queryByText(/no Windows protection details were reported/i)).toBeNull();
    view.rerender(<DeviceSecurityTab device={device({ id: 'device-3' })} managedDeviceId="device-3" active capabilities={[]} />);
    expect(await screen.findByText(/no Windows protection details were reported/i)).toBeTruthy();
  });

  it('security_leave_clears_secret_and_reason', async () => {
    apiMock.mockImplementation(async (path: string) => path.endsWith('/recovery/laps')
      ? response({ status: 'succeeded', data: { id: 'aad-device-1', deviceName: 'WIN-01' } })
      : path.endsWith('/recovery/laps/reveal')
        ? response({ status: 'succeeded', data: { password: 'sensitive-password' } })
        : response(section(protection)));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/protection', expect.anything());
    expect(apiMock.mock.calls.some(([path]) => String(path).includes('/recovery/'))).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/recovery/laps', expect.objectContaining({ cache: 'no-store' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    expect(await screen.findByText('sensitive-password')).toBeTruthy();

    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active={false} capabilities={[]} />);
    expect(screen.queryByText('sensitive-password')).toBeNull();
    expect(screen.queryByLabelText('Reason for recovery access')).toBeNull();
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    expect((await screen.findByLabelText('Reason for recovery access') as HTMLInputElement).value).toBe('');
    expect(screen.queryByText('sensitive-password')).toBeNull();
  });

  it('late_secret_reveal_after_security_leave_is_discarded', async () => {
    const pending = deferred<ReturnType<typeof response>>();
    apiMock.mockImplementation((path: string) => path.endsWith('/recovery/laps/reveal')
      ? pending.promise
      : path.endsWith('/recovery/laps')
        ? Promise.resolve(response({ status: 'succeeded', data: { id: 'aad-device-1', deviceName: 'WIN-01' } }))
        : Promise.resolve(response(section(protection))));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active={false} capabilities={[]} />);
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    await act(async () => pending.resolve(response({ status: 'succeeded', data: { password: 'late-secret' } })));
    expect(screen.queryByText('late-secret')).toBeNull();
    expect((screen.getByLabelText('Reason for recovery access') as HTMLInputElement).value).toBe('');
  });

  it('late_recovery_response_after_tab_switch_is_discarded_and_metadata_can_be_reloaded', async () => {
    const pending = deferred<ReturnType<typeof response>>();
    let metadataRequests = 0;
    apiMock.mockImplementation((path: string) => path.endsWith('/recovery/bitlocker')
      ? ++metadataRequests === 1 ? pending.promise : Promise.resolve(response({ status: 'succeeded', data: [{ id: 'key-for-device-1' }] }))
      : Promise.resolve(response(section(protection))));
    const view = render(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Load BitLocker metadata' }));
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active={false} capabilities={[]} />);
    view.rerender(<DeviceSecurityTab device={device()} managedDeviceId="device-1" active capabilities={[]} />);
    await act(async () => pending.resolve(response({ status: 'succeeded', data: [{ id: 'key-for-device-1' }] })));
    expect(screen.queryByText('Key ID: key-for-device-1')).toBeNull();
    const load = screen.getByRole('button', { name: 'Load BitLocker metadata' }) as HTMLButtonElement;
    expect(load.disabled).toBe(false);
    fireEvent.click(load);
    expect(await screen.findByText('Key ID: key-for-device-1')).toBeTruthy();
  });
});
