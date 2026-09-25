import React from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceDetailPage } from '../../../../src/Web/src/features/devices/DeviceDetailPage';
import { matchRoute } from '../../../../src/Web/src/app/routes';

const apiMock = vi.hoisted(() => vi.fn());
const issueReporter = vi.hoisted(() => ({ report: vi.fn(), clear: vi.fn() }));
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => issueReporter }));
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const response = (body: unknown, status = 200) => ({ ok: status < 400, status, json: async () => body });
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

describe('DeviceDetailPage', () => {
  afterEach(() => { cleanup(); vi.useRealTimers(); vi.clearAllMocks(); window.history.replaceState({}, '', '/devices'); });

  it('retries an unavailable device read without exposing raw errors and clears its issue', async () => {
    apiMock.mockRejectedValueOnce(new Error('raw graph failure')).mockResolvedValue(response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('alert');
    expect(screen.getByRole('heading', { name: 'Device details' })).toBeTruthy();
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'devices:detail:read', kind: 'service' }));
    expect(JSON.stringify(issueReporter.report.mock.calls)).not.toContain('raw graph failure');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByRole('heading', { name: 'WIN-01' });
    expect(issueReporter.clear).toHaveBeenCalledWith('devices:detail:read');
  });

  it('resolves an addressable route and loads the device directly without a preceding search', async () => {
    window.history.replaceState({}, '', '/devices/device-1');
    expect(matchRoute(window.location.pathname).path).toBe('/devices/:id');
    apiMock.mockResolvedValue(response({ id: 'device-1', deviceName: 'WIN-01', operatingSystem: 'Windows', serialNumber: 'serial-1' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    expect(await screen.findByRole('heading', { name: 'WIN-01' })).toBeTruthy();
    expect(screen.getByText('serial-1')).toBeTruthy();
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1', { cache: 'no-store' });
    expect(apiMock).toHaveBeenCalledTimes(1);
  });

  it('requires a reason, makes a no-store POST, and clears LAPS secret after 60 seconds', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === '/api/devices/device-1') return response({ id: 'device-1', deviceName: 'WIN-01', azureAdDeviceId: 'aad-1' });
      if (path.endsWith('/recovery/laps/reveal')) return response({ status: 'succeeded', data: { accountName: 'Admin', password: 'sensitive-password' } });
      return response({ status: 'succeeded', data: { id: 'aad-1', deviceName: 'WIN-01' } });
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    const reveal = screen.getByRole('button', { name: 'Reveal Windows LAPS password' });
    expect((reveal as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    vi.useFakeTimers();
    await act(async () => { fireEvent.click(reveal); await Promise.resolve(); });
    expect(screen.getByText('sensitive-password')).toBeTruthy();
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/recovery/laps/reveal', expect.objectContaining({ method: 'POST', cache: 'no-store', body: JSON.stringify({ reason: 'Incident 123' }) }));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(screen.queryByText('sensitive-password')).toBeNull();
  });

  it('clears a revealed secret immediately on close and on unmount', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01' })
      : response({ status: 'succeeded', data: path.endsWith('/reveal') ? { accountName: 'Admin', password: 'sensitive-password' } : { id: 'aad-1' } }));
    const view = render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    await screen.findByText('sensitive-password');
    fireEvent.click(screen.getByRole('button', { name: 'Close secret' }));
    expect(screen.queryByText('sensitive-password')).toBeNull();
    view.unmount();
    await waitFor(() => expect(screen.queryByText('sensitive-password')).toBeNull());
  });

  it('shows Graph 403 and correlation details for a metadata denial', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01' })
      : response({ status: 'graph_forbidden', graphCorrelationId: 'corr-403', graphRequestId: 'req-403' }, 403));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    const error = await screen.findByRole('alert');
    expect(error.textContent).toContain('lacks access to this specific recovery data');
    expect(error.textContent).toContain('corr-403');
    expect(error.textContent).toContain('req-403');
  });

  it('explains missing recovery scope and disables that recovery control', async () => {
    apiMock.mockResolvedValue(response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" capabilities={[{
      capability: 'devices.bitlocker.metadata', state: 'consent_required', reasonCode: 'delegated_scope_required',
      missingScopes: ['BitlockerKey.ReadBasic.All'], nextStep: { label: 'Grant delegated consent', href: '/api/workspaces/current/consent/start' },
    }]} />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    expect((screen.getByRole('button', { name: 'Load BitLocker metadata' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText(/BitlockerKey.ReadBasic.All/)).toBeTruthy();
    expect(apiMock).toHaveBeenCalledTimes(1);
  });

  it('keeps reveal available with scope and eligible PIM guidance because Graph decides target access', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01' })
      : response({ status: 'succeeded', data: { id: 'aad-1' } }));
    render(<DeviceDetailPage deviceId="device-1" capabilities={[{
      capability: 'devices.laps.reveal', state: 'allowed', reasonCode: 'graph_authoritative',
      nextStep: { label: 'Activate eligible role if Graph denies access', href: '/identity' },
    }]} />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    expect((screen.getByRole('button', { name: 'Reveal Windows LAPS password' }) as HTMLButtonElement).disabled).toBe(false);
    expect(screen.getByText(/Activate eligible role if Graph denies access/)).toBeTruthy();
    expect(screen.getByRole('link', { name: /Activate eligible role if Graph denies access/ }).getAttribute('href')).toBe('/identity');
  });

  it('handles a malformed URL device id as a safe unavailable state', async () => {
    window.history.replaceState({}, '', '/devices/%E0%A4%A');
    render(<DeviceDetailPage />);
    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('discards late metadata from the previous device and clears its recovery reason', async () => {
    const pending = deferred<ReturnType<typeof response>>();
    apiMock.mockImplementation((path: string) => {
      if (path === '/api/devices/device-a/recovery/bitlocker') return pending.promise;
      if (path === '/api/devices/device-a') return Promise.resolve(response({ id: 'device-a', deviceName: 'Device A' }));
      if (path === '/api/devices/device-b') return Promise.resolve(response({ id: 'device-b', deviceName: 'Device B' }));
      return Promise.resolve(response({ status: 'succeeded', data: [] }));
    });
    const view = render(<DeviceDetailPage deviceId="device-a" />);
    await screen.findByRole('heading', { name: 'Device A' });
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident for A' } });
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    view.rerender(<DeviceDetailPage deviceId="device-b" />);
    await screen.findByRole('heading', { name: 'Device B' });
    expect((screen.getByLabelText('Reason for recovery access') as HTMLInputElement).value).toBe('');
    await act(async () => pending.resolve(response({ status: 'succeeded', data: [{ id: 'key-for-A' }] })));
    expect(screen.queryByText('Key ID: key-for-A')).toBeNull();
  });

  it('shows an independent Graph source, retrieval time, and loading state for each section', async () => {
    const bitlocker = deferred<ReturnType<typeof response>>();
    const laps = deferred<ReturnType<typeof response>>();
    apiMock.mockImplementation((path: string) => {
      if (path.endsWith('/recovery/bitlocker')) return bitlocker.promise;
      if (path.endsWith('/recovery/laps')) return laps.promise;
      return Promise.resolve(response({ id: 'device-1', deviceName: 'WIN-01', lastSyncDateTime: '2020-01-01T00:00:00Z' }));
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    expect(screen.getByText(/Source: Microsoft Graph.*Intune managedDevices/)).toBeTruthy();
    expect(screen.getByText(/Device details retrieved:/).textContent).not.toContain('2020');
    expect(screen.getByText(/Source: Microsoft Graph.*BitLocker recovery keys/)).toBeTruthy();
    expect(screen.getByText(/Source: Microsoft Graph.*Windows LAPS/)).toBeTruthy();
    expect(screen.getAllByText(/Not loaded yet/)).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    expect(screen.getByText('Loading BitLocker metadata…')).toBeTruthy();
    expect(screen.getByText('Loading Windows LAPS metadata…')).toBeTruthy();
    await act(async () => bitlocker.resolve(response({ status: 'succeeded', data: [{ id: 'key-1' }] })));
    expect(screen.getByText(/BitLocker metadata retrieved:/)).toBeTruthy();
    expect(screen.getByText('Loading Windows LAPS metadata…')).toBeTruthy();
    await act(async () => laps.resolve(response({ status: 'succeeded', data: { id: 'aad-1' } })));
    expect(screen.getByText(/Windows LAPS metadata retrieved:/)).toBeTruthy();
  });

  it('shows BitLocker retrieval time even when Graph returns no recovery records', async () => {
    apiMock.mockImplementation(async (path: string) => path.endsWith('/recovery/bitlocker')
      ? response({ status: 'succeeded', data: [] })
      : response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    expect(await screen.findByText('No BitLocker recovery record found.')).toBeTruthy();
    expect(screen.getByText(/BitLocker metadata retrieved:/)).toBeTruthy();
  });

  it('keeps BitLocker and LAPS metadata failures independent', async () => {
    apiMock.mockImplementation(async (path: string) => path.endsWith('/recovery/bitlocker')
      ? response({ status: 'graph_forbidden', graphCorrelationId: 'bitlocker-denial' }, 403)
      : path.endsWith('/recovery/laps')
        ? response({ status: 'throttled', graphCorrelationId: 'laps-throttle' }, 429)
        : response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    expect((await screen.findByText(/bitlocker-denial/)).textContent).toContain('lacks access');
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    expect(await screen.findByText(/laps-throttle/)).toBeTruthy();
    expect(screen.getByText(/bitlocker-denial/)).toBeTruthy();
  });

  it('explains why Global Reader access to LAPS metadata does not reveal passwords', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01' })
      : path.endsWith('/recovery/laps/reveal')
        ? response({ status: 'graph_forbidden', guidance: 'laps_password_role_required' }, 403)
        : response({ status: 'succeeded', data: { id: 'aad-1' } }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Global Reader');
    expect(alert.textContent).toContain('metadata');
    expect(alert.textContent).toContain('Cloud Device Administrator');
  });
});
