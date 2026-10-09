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
function openSecurityTab() { fireEvent.click(screen.getByRole('tab', { name: 'Security' })); }
const windowsProtection = {
  antiMalwareVersion: null, controlledConfigurationEnabled: null, deviceState: 'clean', engineVersion: null,
  fullScanOverdue: null, fullScanRequired: null, isVirtualMachine: null, lastFullScanDateTime: null,
  lastFullScanSignatureVersion: null, lastQuickScanDateTime: null, lastQuickScanSignatureVersion: null,
  lastReportedDateTime: '2026-10-08T09:30:00Z', malwareProtectionEnabled: true,
  networkInspectionSystemEnabled: null, productStatus: null, quickScanOverdue: null,
  realTimeProtectionEnabled: true, rebootRequired: null, signatureUpdateOverdue: null,
  signatureVersion: null, tamperProtectionEnabled: null,
};

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
    expect(apiMock).toHaveBeenCalledTimes(2);
  });

  it('direct_route_loads_header_without_directory_search', async () => {
    window.history.replaceState({}, '', '/devices/device-1');
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01', userId: 'user-1', userDisplayName: 'A User', userPrincipalName: 'a@user.example', lastSyncDateTime: '2026-10-08T10:00:00Z' })
      : response({ status: 'succeeded', data: [], retrievedAt: '2026-10-08T10:01:00Z' }));
    render(<DeviceDetailPage />);

    expect(await screen.findByRole('heading', { name: 'WIN-01' })).toBeTruthy();
    expect(apiMock.mock.calls[0][0]).toBe('/api/devices/device-1');
    expect(apiMock.mock.calls.some(([path]) => path === '/api/devices' || path.startsWith('/api/devices?'))).toBe(false);
  });

  it('device_header_uses_reported_user_and_user_domain_only', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01', userId: 'reported-user-id', userDisplayName: 'A User', userPrincipalName: 'a@user.example', managedDeviceOwnerType: 'company' })
      : response({ status: 'succeeded', data: [] }));
    render(<DeviceDetailPage deviceId="device-1" />);

    expect(await screen.findByRole('heading', { name: 'WIN-01' })).toBeTruthy();
    const userLinks = screen.getAllByRole('link', { name: 'A User' });
    expect(userLinks.map(userLink => userLink.getAttribute('href'))).toEqual(['/users/reported-user-id', '/users/reported-user-id']);
    expect(screen.getByText('User domain').parentElement?.textContent).toContain('user.example');
    expect(screen.getByText('Company owned')).toBeTruthy();
    fireEvent.click(screen.getByRole('tab', { name: 'Security' }));
    expect(screen.getByText('User domain: user.example')).toBeTruthy();
    expect(screen.getAllByRole('link', { name: 'A User' })).toHaveLength(1);
  });

  it('missing_user_and_malformed_upn_are_distinct', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'No user device' })
      : response({ status: 'succeeded', data: [] }));
    const view = render(<DeviceDetailPage deviceId="device-1" />);
    expect(await screen.findByRole('heading', { name: 'No user device' })).toBeTruthy();
    expect(screen.getByText('No primary user')).toBeTruthy();
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-2'
      ? response({ id: 'device-2', deviceName: 'Unreadable user', userId: 'user-2', userDisplayName: null, userPrincipalName: 'not-an-upn' })
      : response({ status: 'succeeded', data: [] }));
    view.rerender(<DeviceDetailPage deviceId="device-2" />);
    expect(await screen.findByRole('heading', { name: 'Unreadable user' })).toBeTruthy();
    expect(screen.getByText('Primary user unavailable')).toBeTruthy();
    expect(screen.getByText('User domain').parentElement?.textContent).toContain('Unavailable');
    expect(document.body.textContent).not.toContain('device-2.example');
  });

  it('policy_request_is_lazy_and_retry_is_section_local', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === '/api/devices/device-1') return response({ id: 'device-1', deviceName: 'WIN-01', complianceState: 'noncompliant' });
      if (path === '/api/devices/device-1/compliance-policies') return response({ status: 'graph_forbidden', data: null, error: { category: 'authorization', message: 'denied', state: 'graph_forbidden' } }, 403);
      return response({ status: 'succeeded', data: [] });
    });

    render(<DeviceDetailPage deviceId="device-1" />);
    expect(await screen.findByRole('heading', { name: 'WIN-01' })).toBeTruthy();
    await screen.findByText(/not allowed to read per-policy compliance reports/i);
    expect(apiMock.mock.calls.map(([path]) => path)).toEqual(['/api/devices/device-1', '/api/devices/device-1/compliance-policies']);
    fireEvent.click(screen.getByRole('button', { name: 'Retry policies' }));
    await waitFor(() => expect(apiMock.mock.calls.filter(([path]) => path === '/api/devices/device-1/compliance-policies')).toHaveLength(2));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await waitFor(() => expect(apiMock.mock.calls.filter(([path]) => path.endsWith('/configuration/reported'))).toHaveLength(1));
    expect(apiMock.mock.calls.some(([path]) => path.endsWith('/apps'))).toBe(false);
    fireEvent.click(screen.getByRole('tab', { name: 'Apps' }));
    await waitFor(() => expect(apiMock.mock.calls.filter(([path]) => path.endsWith('/apps'))).toHaveLength(1));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    expect(apiMock.mock.calls.filter(([path]) => path.endsWith('/configuration/reported'))).toHaveLength(1);
    expect(document.querySelector('.workspace-page-header__meta')?.textContent).toContain('Compliance: Noncompliant');
  });

  it('protection_is_requested_only_on_security_activation_and_recovery_stays_user_initiated', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === '/api/devices/device-1') return response({ id: 'device-1', deviceName: 'WIN-01', operatingSystem: 'Windows', isEncrypted: true });
      if (path.endsWith('/compliance-policies')) return response({ status: 'succeeded', data: [], retrievedAt: null, partialData: false, error: null });
      if (path.endsWith('/protection')) return response({ status: 'succeeded', data: windowsProtection, retrievedAt: '2026-10-08T10:00:00Z', partialData: false, error: null });
      return response({ status: 'succeeded', data: [] });
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    await screen.findByText('No per-policy compliance reports were returned.');
    expect(apiMock.mock.calls.some(([path]) => String(path).endsWith('/protection'))).toBe(false);
    expect(apiMock.mock.calls.some(([path]) => String(path).includes('/recovery/'))).toBe(false);

    openSecurityTab();
    expect(await screen.findByText('Malware protection enabled')).toBeTruthy();
    expect(document.querySelector('time[datetime="2026-10-08T09:30:00Z"]')).toBeTruthy();
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/protection', { cache: 'no-store' });
    expect(apiMock.mock.calls.some(([path]) => String(path).includes('/recovery/'))).toBe(false);
    fireEvent.click(screen.getByRole('tab', { name: 'Actions' }));
    expect(apiMock.mock.calls.some(([path]) => String(path).includes('/recovery/'))).toBe(false);
  });

  it('shows_a_retryable_unavailable_state_when_policy_response_is_valid_json_null', async () => {
    apiMock.mockImplementation(async (path: string) => path === '/api/devices/device-1'
      ? response({ id: 'device-1', deviceName: 'WIN-01' })
      : response(null));
    render(<DeviceDetailPage deviceId="device-1" />);

    await screen.findByRole('heading', { name: 'WIN-01' });
    expect(await screen.findByText(/per-policy compliance reports are unavailable/i)).toBeTruthy();
    expect(screen.queryByText('Loading per-policy compliance reports…')).toBeNull();
    expect(screen.getByRole('button', { name: 'Retry policies' })).toBeTruthy();
  });

  it('keeps_the_overview_bound_to_the_current_device_when_a_policy_read_finishes_late', async () => {
    const pending = deferred<ReturnType<typeof response>>();
    apiMock.mockImplementation((path: string) => {
      if (path === '/api/devices/device-a') return Promise.resolve(response({ id: 'device-a', deviceName: 'Device A' }));
      if (path === '/api/devices/device-b') return Promise.resolve(response({ id: 'device-b', deviceName: 'Device B' }));
      if (path === '/api/devices/device-a/compliance-policies') return pending.promise;
      return Promise.resolve(response({ status: 'succeeded', data: [] }));
    });
    const view = render(<DeviceDetailPage deviceId="device-a" />);
    await screen.findByRole('heading', { name: 'Device A' });
    view.rerender(<DeviceDetailPage deviceId="device-b" />);
    await screen.findByRole('heading', { name: 'Device B' });
    await act(async () => pending.resolve(response({ status: 'succeeded', data: [{ id: 'old-policy', displayName: 'Policy for A' }] })));
    expect(screen.queryByText('Policy for A')).toBeNull();
  });

  it('discards_policy_results_when_leaving_the_tab_and_reloads_on_return', async () => {
    const pending = deferred<ReturnType<typeof response>>();
    let policyRequests = 0;
    apiMock.mockImplementation((path: string) => {
      if (path === '/api/devices/device-1') return Promise.resolve(response({ id: 'device-1', deviceName: 'WIN-01' }));
      if (path.endsWith('/compliance-policies')) {
        policyRequests += 1;
        return policyRequests === 1 ? pending.promise : Promise.resolve(response({ status: 'succeeded', data: [] }));
      }
      return Promise.resolve(response({ status: 'succeeded', data: [] }));
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    await waitFor(() => expect(policyRequests).toBe(1));
    fireEvent.click(screen.getByRole('tab', { name: 'Configuration' }));
    await act(async () => pending.resolve(response({ status: 'succeeded', data: [{ id: 'stale-policy', displayName: 'Stale policy' }] })));
    fireEvent.click(screen.getByRole('tab', { name: 'Overview' }));
    await waitFor(() => expect(policyRequests).toBe(2));
    expect(screen.queryByText('Stale policy')).toBeNull();
  });

  it('clears_revealed_secrets_when_leaving_security', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === '/api/devices/device-1') return response({ id: 'device-1', deviceName: 'WIN-01' });
      if (path.endsWith('/recovery/laps/reveal')) return response({ status: 'succeeded', data: { password: 'sensitive-password' } });
      if (path.endsWith('/recovery/laps')) return response({ status: 'succeeded', data: { id: 'aad-1', deviceName: 'WIN-01' } });
      return response({ status: 'succeeded', data: [] });
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    openSecurityTab();
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    await screen.findByText('sensitive-password');
    fireEvent.click(screen.getByRole('tab', { name: 'Actions' }));
    expect(screen.queryByText('sensitive-password')).toBeNull();
  });

  it('supports_roving_keyboard_tabs_with_arrows_home_and_end', async () => {
    apiMock.mockResolvedValue(response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    const overview = screen.getByRole('tab', { name: 'Overview' });
    for (const tab of screen.getAllByRole('tab')) expect(document.getElementById(tab.getAttribute('aria-controls')!)).toBeTruthy();
    overview.focus();
    fireEvent.keyDown(overview, { key: 'End' });
    const actions = screen.getByRole('tab', { name: 'Actions' });
    expect(actions.getAttribute('aria-selected')).toBe('true');
    expect(actions.getAttribute('tabindex')).toBe('0');
    expect(document.activeElement).toBe(actions);
    fireEvent.keyDown(actions, { key: 'Home' });
    expect(screen.getByRole('tab', { name: 'Overview' }).getAttribute('aria-selected')).toBe('true');
  });

  it('requires a reason, makes a no-store POST, and clears LAPS secret after 60 seconds', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === '/api/devices/device-1') return response({ id: 'device-1', deviceName: 'WIN-01', azureAdDeviceId: 'aad-1' });
      if (path.endsWith('/recovery/laps/reveal')) return response({ status: 'succeeded', data: { accountName: 'Admin', password: 'sensitive-password' } });
      return response({ status: 'succeeded', data: { id: 'aad-1', deviceName: 'WIN-01' } });
    });
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    openSecurityTab();
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
    openSecurityTab();
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
    openSecurityTab();
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
    openSecurityTab();
    expect((screen.getByRole('button', { name: 'Load BitLocker metadata' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText(/isn't available to you/)).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Open PIM guidance' })).toBeTruthy();
    expect(document.body.textContent).not.toContain('BitlockerKey.');
    expect(apiMock).toHaveBeenCalledTimes(2);
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
    openSecurityTab();
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
    openSecurityTab();
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident for A' } });
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    view.rerender(<DeviceDetailPage deviceId="device-b" />);
    await screen.findByRole('heading', { name: 'Device B' });
    openSecurityTab();
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
    openSecurityTab();
    expect(document.body.textContent).toContain('Microsoft Graph');
    expect(document.body.textContent).not.toContain('Source:');
    expect(document.querySelector('.workspace-page-header__meta')!.textContent).toContain('2020');
    expect(screen.getAllByText(/Not loaded yet/)).toHaveLength(2);
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    expect(screen.getByText('Loading BitLocker metadata…')).toBeTruthy();
    expect(screen.getByText('Loading Windows LAPS metadata…')).toBeTruthy();
    await act(async () => bitlocker.resolve(response({ status: 'succeeded', data: [{ id: 'key-1' }] })));
    expect(screen.getByText(/BitLocker metadata retrieved/)).toBeTruthy();
    expect(screen.getByText('Loading Windows LAPS metadata…')).toBeTruthy();
    await act(async () => laps.resolve(response({ status: 'succeeded', data: { id: 'aad-1' } })));
    expect(screen.getByText(/Windows LAPS metadata retrieved/)).toBeTruthy();
  });

  it('shows BitLocker retrieval time even when Graph returns no recovery records', async () => {
    apiMock.mockImplementation(async (path: string) => path.endsWith('/recovery/bitlocker')
      ? response({ status: 'succeeded', data: [] })
      : response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    openSecurityTab();
    fireEvent.click(screen.getByRole('button', { name: 'Load BitLocker metadata' }));
    expect(await screen.findByText('No BitLocker recovery record found.')).toBeTruthy();
    expect(screen.getByText(/BitLocker metadata retrieved/)).toBeTruthy();
  });

  it('keeps BitLocker and LAPS metadata failures independent', async () => {
    apiMock.mockImplementation(async (path: string) => path.endsWith('/recovery/bitlocker')
      ? response({ status: 'graph_forbidden', graphCorrelationId: 'bitlocker-denial' }, 403)
      : path.endsWith('/recovery/laps')
        ? response({ status: 'throttled', graphCorrelationId: 'laps-throttle' }, 429)
        : response({ id: 'device-1', deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId="device-1" />);
    await screen.findByRole('heading', { name: 'WIN-01' });
    openSecurityTab();
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
    openSecurityTab();
    fireEvent.click(screen.getByRole('button', { name: 'Load Windows LAPS metadata' }));
    await screen.findByText('Windows LAPS metadata loaded');
    fireEvent.change(screen.getByLabelText('Reason for recovery access'), { target: { value: 'Incident 123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Reveal Windows LAPS password' }));
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain('Global Reader');
    expect(alert.textContent).toContain('metadata');
    expect(alert.textContent).toContain('Cloud Device Administrator');
  });

  const guid = '3f2b8c1e-1111-2222-3333-444455556666';
  const manage = [{ capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'ok' }] as never;

  it('keeps identifiers out of the overview and inside collapsed technical details', async () => {
    apiMock.mockResolvedValue(response({ id: guid, deviceName: 'WIN-01', userId: 'user-guid-1', complianceState: 'compliant' }));
    render(<DeviceDetailPage deviceId={guid} />);
    await screen.findByRole('heading', { level: 1, name: 'WIN-01' });
    const overview = screen.getByRole('heading', { name: 'Essentials' }).closest('section')!;
    expect(overview.textContent).not.toContain(guid);
    const details = screen.getByText('Technical details').closest('details')!;
    expect(details.open).toBe(false);
    expect(details.textContent).toContain(guid);
    expect(document.body.textContent).not.toContain('Source:');
  });

  it('shows an unnamed placeholder instead of the id and a working back link', async () => {
    const onNavigate = vi.fn();
    apiMock.mockResolvedValue(response({ id: guid, deviceName: '' }));
    render(<DeviceDetailPage deviceId={guid} onNavigate={onNavigate} />);
    expect((await screen.findByRole('heading', { level: 1 })).textContent).toBe('Unnamed device');
    fireEvent.click(screen.getByRole('link', { name: '← Back to Devices' }));
    expect(onNavigate).toHaveBeenCalledWith('/devices');
  });

  it('puts the danger zone last with only retire and wipe, and keeps confirmations', async () => {
    apiMock.mockResolvedValue(response({ id: guid, deviceName: 'WIN-01' }));
    render(<DeviceDetailPage deviceId={guid} capabilities={manage} />);
    await screen.findByRole('heading', { level: 1, name: 'WIN-01' });
    fireEvent.click(screen.getByRole('tab', { name: 'Actions' }));
    const actions = screen.getByRole('heading', { name: 'Device actions' });
    const danger = screen.getByRole('heading', { name: 'Danger zone' });
    expect(actions.compareDocumentPosition(danger) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const dangerButtons = Array.from(danger.closest('section')!.querySelectorAll('button')).map(b => b.textContent);
    expect(dangerButtons).toEqual(['Retire device', 'Wipe device']);
    fireEvent.click(screen.getByRole('button', { name: 'Restart device' }));
    expect(screen.getByRole('dialog')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(danger.closest('section')!.querySelectorAll('button')[1]);
    const confirm = screen.getAllByRole('button', { name: 'Wipe device' }).find(b => b.closest('[role="dialog"]')) as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
  });
});
