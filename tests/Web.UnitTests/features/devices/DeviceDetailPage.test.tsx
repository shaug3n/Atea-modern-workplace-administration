import React from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceDetailPage } from '../../../../src/Web/src/features/devices/DeviceDetailPage';
import { matchRoute } from '../../../../src/Web/src/app/routes';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const response = (body: unknown, status = 200) => ({ ok: status < 400, status, json: async () => body });

describe('DeviceDetailPage', () => {
  afterEach(() => { cleanup(); vi.useRealTimers(); vi.clearAllMocks(); window.history.replaceState({}, '', '/devices'); });

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
  });

  it('handles a malformed URL device id as a safe unavailable state', async () => {
    window.history.replaceState({}, '', '/devices/%E0%A4%A');
    render(<DeviceDetailPage />);
    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });
});
