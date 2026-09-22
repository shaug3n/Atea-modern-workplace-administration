import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DevicesPage } from '../../../../src/Web/src/features/devices/DevicesPage';
import { executeDeviceAction, type DeviceAction } from '../../../../src/Web/src/features/devices/devicesApi';
import type { ApiFetch } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('DevicesPage', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  const devices = {
    items: [
      { id: 'device-1', deviceName: 'WIN-TEST-01', operatingSystem: 'Windows', complianceState: 'compliant', managedDeviceOwnerType: 'company', lastSyncDateTime: '2026-09-22T08:00:00Z', manufacturer: 'Contoso', model: 'Model X' },
    ],
    total: 1,
    fetchedAt: '2026-09-22T08:05:00Z',
    freshness: 'live',
    partialData: false,
    access: { state: 'allowed' },
  } as const;

  const loadDevices = async () => devices;

  it('renders operational device summary and keeps actions hidden for a read-only user', async () => {
    render(<DevicesPage
      capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'read_only', reasonCode: 'role_read_only' }]}
      loadDevices={async () => ({
        items: [
          { id: 'device-1', deviceName: 'LAPTOP-01', operatingSystem: 'Windows', complianceState: 'compliant', managedDeviceOwnerType: 'company', lastSyncDateTime: '2026-09-22T08:00:00Z', manufacturer: 'Contoso', model: 'Model X' },
          { id: 'device-2', deviceName: 'LAPTOP-02', operatingSystem: 'Windows', complianceState: 'noncompliant', managedDeviceOwnerType: 'company', lastSyncDateTime: '2026-09-21T08:00:00Z', manufacturer: 'Contoso', model: 'Model Y' },
        ],
        total: 2,
        fetchedAt: '2026-09-22T08:05:00Z',
        freshness: 'live',
        partialData: false,
        access: { state: 'allowed' },
      })}
    />);

    await waitFor(() => expect(screen.getByText('LAPTOP-01')).toBeTruthy());
    expect(screen.getByText('Managed devices')).toBeTruthy();
    expect(screen.getByText('2')).toBeTruthy();
    expect(screen.getAllByText('Compliant').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Noncompliant').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Sync' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remote lock' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Open details for LAPTOP-01' }));
    expect(screen.getByRole('dialog').textContent).toContain('LAPTOP-01');
    expect(screen.getByText('Management state')).toBeTruthy();
  });

  it('surfaces an embedded Graph permission error instead of showing an empty result', async () => {
    render(<DevicesPage
      capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]}
      loadDevices={async () => ({
        items: [],
        total: 0,
        fetchedAt: '2026-09-22T08:05:00Z',
        freshness: 'unavailable',
        partialData: true,
        access: { state: 'hidden' },
        error: { category: 'capability_required', message: 'Managed devices are not available for the current role or delegated permissions.' },
      })}
    />);

    expect(await screen.findByText('Managed devices are unavailable. Check delegated permissions and try again.')).toBeTruthy();
    expect(screen.getAllByText('Managed devices are not available for the current role or delegated permissions.').length).toBeGreaterThan(0);
    expect(screen.queryByText('No managed devices match the current filters.')).toBeNull();
  });

  it('shows a PIM handoff instead of calling Intune while device access is gated', async () => {
    const loadDevices = vi.fn(async () => ({
      items: [],
      total: 0,
      fetchedAt: '2026-09-22T08:05:00Z',
      freshness: 'unavailable',
      partialData: true,
      access: { state: 'pim_activation_required' },
    }));

    render(<DevicesPage
      capabilities={[{
        capability: 'devices.view',
        state: 'pim_activation_required',
        reasonCode: 'pim_activation_required',
        nextStep: { label: 'Activate the required Entra role', href: '/identity' },
      }]}
      loadDevices={loadDevices}
    />);

    expect(await screen.findByRole('heading', { name: 'Activate an Entra role to view devices' })).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Activate the required Entra role' }).getAttribute('href')).toBe('/identity');
    expect(loadDevices).not.toHaveBeenCalled();
  });

  it.each([
    ['consent_required', 'Microsoft Graph consent is required'],
    ['pim_activation_required', 'Activate an Entra role to view devices'],
    ['pim_approval_required', 'waiting for PIM approval'],
    ['pim_mfa_required', 'Complete MFA for PIM activation'],
    ['pim_eligibility_expired', 'PIM eligibility has expired'],
    ['temporarily_unavailable', 'authorization could not be verified'],
    ['hidden', 'Permission required'],
  ] as const)('does not fetch devices when view capability is %s', async (state, expected) => {
    const loader = vi.fn(async () => devices);
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state, reasonCode: state }]} loadDevices={loader} />);

    expect(await screen.findByRole('heading', { name: 'Devices' })).toBeTruthy();
    if (state === 'hidden') {
      expect(screen.queryByText('Loading managed devices…')).toBeNull();
    } else {
      expect(screen.getAllByRole('status').some(element => new RegExp(expected, 'i').test(element.textContent ?? ''))).toBe(true);
    }
    expect(loader).not.toHaveBeenCalled();
  });

  it('places all five privileged device actions in one accessible action menu per row', async () => {
    render(<DevicesPage
      capabilities={[
        { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' },
      ]}
      loadDevices={loadDevices}
    />);

    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    expect(screen.getAllByRole('menu')).toHaveLength(1);
    expect(screen.getByRole('menuitem', { name: 'Sync device' })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: 'Remote lock' })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: 'Restart device' })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: 'Retire device' })).toBeTruthy();
    expect(screen.getByRole('menuitem', { name: 'Wipe device' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sync' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remote lock' })).toBeNull();
  });

  it('shows privileged permission state without leaving failing action buttons for consent or PIM users', async () => {
    render(<DevicesPage
      capabilities={[
        { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'devices.privileged.manage', state: 'consent_required', reasonCode: 'missing_scope', missingScopes: ['DeviceManagementManagedDevices.PrivilegedOperations.All'] },
      ]}
      loadDevices={loadDevices}
    />);

    await screen.findByText('WIN-TEST-01');
    expect(screen.getByText(/delegated Microsoft Graph consent is required/i)).toBeTruthy();
    expect(screen.queryByRole('button', { name: /Actions for WIN-TEST-01/i })).toBeNull();
    expect(screen.getByRole('button', { name: /Open details for WIN-TEST-01/i })).toBeTruthy();
  });

  it('groups device details into Overview, Security and Danger zone', async () => {
    render(<DevicesPage
      capabilities={[
        { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' },
      ]}
      loadDevices={loadDevices}
    />);

    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Open details for WIN-TEST-01' }));
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Security' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Danger zone' })).toBeTruthy();
  });

  it('keeps one details control and one action menu per device row', async () => {
    const multiRowDevices = { ...devices, items: [...devices.items, { ...devices.items[0], id: 'device-2', deviceName: 'WIN-TEST-02' }], total: 2 };
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={async () => multiRowDevices} />);

    await screen.findByText('WIN-TEST-02');
    expect(screen.getAllByRole('button', { name: /Open details for/ })).toHaveLength(2);
    expect(screen.getAllByRole('button', { name: /Actions for/ })).toHaveLength(2);
  });

  it('closes the action menu on Escape and restores trigger focus', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    const trigger = screen.getByRole('button', { name: 'Actions for WIN-TEST-01' });
    fireEvent.click(trigger);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('menu')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('closes the details panel on Escape', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Open details for WIN-TEST-01' }));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it.each([
    ['Remote lock', 'REMOTE LOCK'],
    ['Restart device', 'RESTART'],
    ['Retire device', 'RETIRE'],
    ['Wipe device', 'WIPE'],
  ])('requires the exact %s confirmation phrase', async (label, phrase) => {
    render(<DevicesPage
      capabilities={[
        { capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' },
      ]}
      loadDevices={loadDevices}
    />);

    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: label }));
    expect(screen.getByRole('dialog')).toBeTruthy();
    expect(screen.getByLabelText(/type .* to confirm/i)).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByLabelText(/type .* to confirm/i), { target: { value: phrase } });
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('requires review but no phrase for Sync and submits only the selected typed action', async () => {
    apiMock.mockResolvedValue({ ok: true, json: async () => ({ status: 'queued' }) });
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sync device' }));
    expect(screen.queryByLabelText(/type .* to confirm/i)).toBeNull();
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/actions/sync', expect.objectContaining({ method: 'POST' })));
    expect(apiMock.mock.calls.some(([url]) => String(url).includes('/restart'))).toBe(false);
  });

  it('keeps the confirm dialog open while busy and ignores duplicate submit and Escape', async () => {
    let resolveRequest!: (value: unknown) => void;
    apiMock.mockReturnValue(new Promise(resolve => { resolveRequest = resolve; }));
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sync device' }));
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    const confirm = screen.getByRole('button', { name: 'Confirm action' });
    fireEvent.click(confirm);
    fireEvent.click(confirm);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(apiMock).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('dialog')).toBeTruthy();
    resolveRequest({ ok: true, json: async () => ({ status: 'queued' }) });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('clears busy state and shows a safe submission error when the action request rejects', async () => {
    apiMock.mockRejectedValue(new Error('raw transport detail'));
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sync device' }));
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be submitted/i);
    expect(screen.getByRole('alert').textContent).not.toContain('raw transport detail');
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps wrong-case and partial-whitespace phrases disabled', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Wipe device' }));
    const phraseInput = screen.getByLabelText(/type .* to confirm/i);
    fireEvent.change(phraseInput, { target: { value: ' wipe ' } });
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    expect((screen.getByRole('button', { name: 'Confirm action' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it.each([
    ['sync', '/api/devices/device%2F1/actions/sync'],
    ['remote-lock', '/api/devices/device%2F1/actions/remote-lock'],
    ['restart', '/api/devices/device%2F1/actions/restart'],
    ['retire', '/api/devices/device%2F1/actions/retire'],
    ['wipe', '/api/devices/device%2F1/actions/wipe'],
  ] as const)('sends a typed %s request with a POST idempotency contract', async (action, expectedUrl) => {
    const api = vi.fn(async () => ({ ok: true, json: async () => ({ status: 'queued' }) }));
    await executeDeviceAction(api as unknown as ApiFetch, 'device/1', action as DeviceAction);
    expect(api).toHaveBeenCalledWith(expectedUrl, expect.objectContaining({ method: 'POST', headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }) }));
    expect((api.mock.calls[0][1] as RequestInit).headers && String((api.mock.calls[0][1] as RequestInit).headers && ((api.mock.calls[0][1] as RequestInit).headers as Record<string, string>)['Idempotency-Key']).length).toBeGreaterThan(0);
  });
});
