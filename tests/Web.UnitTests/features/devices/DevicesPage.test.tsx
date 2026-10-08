import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DevicesPage } from '../../../../src/Web/src/features/devices/DevicesPage';
import { executeDeviceAction, type DeviceAction } from '../../../../src/Web/src/features/devices/devicesApi';
import type { ApiFetch } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());
const issueReporter = vi.hoisted(() => ({ report: vi.fn(), clear: vi.fn() }));
vi.mock('../../../../src/Web/src/notifications/WorkspaceNotifications', () => ({ useWorkspaceIssueReporter: () => issueReporter }));

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('DevicesPage', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  it('keeps filters after a failed read and clears the service issue on retry', async () => {
    const loader = vi.fn().mockRejectedValueOnce(new Error('raw graph failure')).mockResolvedValue(devices);
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loader} />);
    fireEvent.change(screen.getByLabelText('Search devices'), { target: { value: 'WIN' } });
    await screen.findByRole('alert');
    expect((screen.getByLabelText('Search devices') as HTMLInputElement).value).toBe('WIN');
    expect(issueReporter.report).toHaveBeenCalledWith(expect.objectContaining({ key: 'devices:read', kind: 'service' }));
    expect(JSON.stringify(issueReporter.report.mock.calls)).not.toContain('raw graph failure');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByText('WIN-TEST-01');
    expect(issueReporter.clear).toHaveBeenCalledWith('devices:read');
    expect(screen.getByRole('table', { name: 'Managed device results' }).textContent).toContain('WIN-TEST-01');
  });

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

  it('hides prior rows and dialogs as soon as a new device query starts', async () => {
    let resolveNext: ((value: typeof devices) => void) | undefined;
    const loader = vi.fn().mockResolvedValueOnce(devices).mockImplementation(() => new Promise(resolve => { resolveNext = resolve; }));
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loader} />);
    await screen.findByRole('link', { name: 'WIN-TEST-01' });
    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    fireEvent.change(screen.getByLabelText('Search devices'), { target: { value: 'new' } });
    expect(screen.queryByRole('button', { name: 'Open details for WIN-TEST-01' })).toBeNull();
    expect(screen.queryByRole('dialog', { name: 'WIN-TEST-01' })).toBeNull();
    resolveNext?.({ ...devices, items: [] });
  });

  it('does not revive a prior result when filters return to the same values before a fresh response', async () => {
    const loader = vi.fn().mockResolvedValueOnce(devices).mockImplementation(() => new Promise(() => {}));
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loader} />);
    await screen.findByRole('link', { name: 'WIN-TEST-01' });
    const search = screen.getByLabelText('Search devices');
    fireEvent.change(search, { target: { value: 'other' } });
    fireEvent.change(search, { target: { value: '' } });
    expect(screen.queryByRole('button', { name: 'Open details for WIN-TEST-01' })).toBeNull();
  });

  it('shows labelled compact device details and no mutation menu for a reader', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true, addEventListener: () => {}, removeEventListener: () => {} }));
    const longId = 'device-' + 'x'.repeat(90);
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'read_only', reasonCode: 'role_read_only' }]} loadDevices={async () => ({ ...devices, items: [{ ...devices.items[0], id: longId }] })} />);
    const compact = await screen.findByRole('list', { name: 'Devices' });
    expect(compact.textContent).toContain(longId);
    expect(compact.textContent).toContain('Compliance');
    expect(compact.querySelector('a[href^="/devices/"]')).toBeTruthy();
    expect(compact.querySelector('[role="menu"]')).toBeNull();
  });

  it('exports active device filters and shows completion metadata', async () => {
    apiMock.mockResolvedValue(new Response('"Id"\n"device-1"\n', { status: 200, headers: { 'X-Export-Row-Count': '1', 'X-Export-Max-Rows': '10000', 'X-Export-Truncated': 'false', 'Content-Type': 'text/csv' } }));
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:test'), revokeObjectURL: vi.fn() }));
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);

    fireEvent.change(screen.getByLabelText('Search devices'), { target: { value: 'WIN' } });
    fireEvent.click(screen.getByRole('button', { name: 'Export filtered CSV' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith(expect.stringContaining('/api/devices/export.csv?search=WIN'), expect.anything()));
    expect(await screen.findByText(/1 row exported.*Complete filtered result/i)).toBeTruthy();
  });

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
    fireEvent.click(screen.getByRole('link', { name: 'LAPTOP-01' }));
    expect(screen.getByRole('dialog').textContent).toContain('LAPTOP-01');
    expect(screen.getByText('Management state')).toBeTruthy();
  });

  it('compliance_tiles_toggle_and_clear_only_their_filter', async () => {
    const loader = vi.fn(async () => ({
      ...devices,
      items: [
        { ...devices.items[0], deviceName: 'LAPTOP-01', complianceState: 'compliant' },
        { ...devices.items[0], id: 'device-2', deviceName: 'LAPTOP-02', complianceState: 'noncompliant' },
      ],
    }));
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loader} />);
    await screen.findByRole('link', { name: 'LAPTOP-01' });
    fireEvent.change(screen.getByLabelText('Search devices'), { target: { value: 'LAP' } });
    fireEvent.change(screen.getByLabelText('Operating system'), { target: { value: 'Windows' } });
    await waitFor(() => expect(loader).toHaveBeenLastCalledWith({ search: 'LAP', complianceState: '', operatingSystem: 'Windows' }, null));

    let compliantTile = screen.getByRole('button', { name: /Compliant/ });
    expect(compliantTile.tagName).toBe('BUTTON');
    expect((compliantTile as HTMLButtonElement).type).toBe('button');
    fireEvent.click(compliantTile);
    await waitFor(() => expect(loader).toHaveBeenLastCalledWith({ search: 'LAP', complianceState: 'compliant', operatingSystem: 'Windows' }, null));
    compliantTile = await screen.findByRole('button', { name: /Compliant/ });
    expect(compliantTile.getAttribute('aria-pressed')).toBe('true');

    fireEvent.click(compliantTile);
    await waitFor(() => expect(loader).toHaveBeenLastCalledWith({ search: 'LAP', complianceState: '', operatingSystem: 'Windows' }, null));
    compliantTile = await screen.findByRole('button', { name: /Compliant/ });
    expect(compliantTile.getAttribute('aria-pressed')).toBe('false');

    const noncompliantTile = screen.getByRole('button', { name: /Noncompliant/ });
    fireEvent.click(noncompliantTile);
    await waitFor(() => expect(loader).toHaveBeenLastCalledWith({ search: 'LAP', complianceState: 'noncompliant', operatingSystem: 'Windows' }, null));
    expect((await screen.findByRole('button', { name: /Noncompliant/ })).getAttribute('aria-pressed')).toBe('true');
    expect((await screen.findByRole('button', { name: /^Compliant/ })).getAttribute('aria-pressed')).toBe('false');

    fireEvent.change(screen.getByLabelText('Compliance'), { target: { value: 'compliant' } });
    await waitFor(() => expect(loader).toHaveBeenLastCalledWith({ search: 'LAP', complianceState: 'compliant', operatingSystem: 'Windows' }, null));
    expect((await screen.findByRole('button', { name: /^Compliant/ })).getAttribute('aria-pressed')).toBe('true');
    expect((await screen.findByRole('button', { name: /Noncompliant/ })).getAttribute('aria-pressed')).toBe('false');
  });

  it('compliance_tiles_are_labeled_as_current_page_counts', async () => {
    render(<DevicesPage
      capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]}
      loadDevices={async () => ({
        ...devices,
        total: 42,
        items: [
          { ...devices.items[0], deviceName: 'LAPTOP-01', complianceState: 'compliant' },
          { ...devices.items[0], id: 'device-2', deviceName: 'LAPTOP-02', complianceState: 'noncompliant' },
        ],
      })}
    />);

    const compliantTile = await screen.findByRole('button', { name: /Compliant/ });
    expect(within(compliantTile).getByText('1')).toBeTruthy();
    expect(within(compliantTile).getByText(/loaded page/i)).toBeTruthy();
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
    expect(screen.getByText('Device data is unavailable. Check Notifications for details.')).toBeTruthy();
    expect(screen.queryByText('Managed devices are not available for the current role or delegated permissions.')).toBeNull();
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

  it('lists routine actions first and destructive actions after a separator in the row menu', async () => {
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
    expect(screen.getByRole('menuitem', { name: /Open details/ })).toBeTruthy();
    const items = screen.getAllByRole('menuitem').map(item => item.textContent ?? '');
    expect(items.findIndex(text => text.startsWith('Retire device'))).toBeGreaterThan(items.findIndex(text => text.startsWith('Restart device')));
    expect(screen.getByRole('menuitem', { name: /Wipe device/ })).toBeTruthy();
    expect(document.querySelector('.action-menu__separator, [role="separator"]')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Sync' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remote lock' })).toBeNull();

    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    expect(screen.getByRole('button', { name: 'Retire device' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Wipe device' })).toBeTruthy();
  });

  it('opens the device from a valid query id after the device list loads', async () => {
    window.history.pushState({}, '', '/devices?device=device-1');
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);

    expect(await screen.findByRole('dialog', { name: 'WIN-TEST-01' })).toBeTruthy();
    window.history.pushState({}, '', '/devices');
  });

  it('ignores unknown and malformed device query ids without bypassing the loaded list', async () => {
    window.history.pushState({}, '', '/devices?device=%00');
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);

    await screen.findByText('WIN-TEST-01');
    expect(screen.queryByRole('dialog')).toBeNull();
    window.history.pushState({}, '', '/devices');
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
    expect(screen.getByRole('link', { name: 'WIN-TEST-01' })).toBeTruthy();
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
    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    expect(screen.getByRole('heading', { name: 'Overview' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Security' })).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Danger zone' })).toBeTruthy();
  });

  it('keeps one details control and one action menu per device row', async () => {
    const multiRowDevices = { ...devices, items: [...devices.items, { ...devices.items[0], id: 'device-2', deviceName: 'WIN-TEST-02' }], total: 2 };
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={async () => multiRowDevices} />);

    await screen.findByText('WIN-TEST-02');
    expect(screen.getAllByRole('link', { name: /WIN-TEST-/ })).toHaveLength(2);
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
    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('focuses and contains the device detail panel, then restores the row trigger', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    const trigger = screen.getByRole('link', { name: 'WIN-TEST-01' });
    fireEvent.click(trigger);
    const panel = screen.getByRole('dialog', { name: 'WIN-TEST-01' });
    const close = screen.getByRole('button', { name: 'Close device details' });
    expect(document.activeElement).toBe(close);
    fireEvent.keyDown(panel, { key: 'Tab', shiftKey: true });
    expect(document.activeElement).toBe(close);
    fireEvent.click(close);
    expect(document.activeElement).toBe(trigger);
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
    if (label === 'Retire device' || label === 'Wipe device') {
      fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
      fireEvent.click(screen.getByRole('button', { name: label }));
    } else {
      fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
      fireEvent.click(screen.getByRole('menuitem', { name: label }));
    }
    expect(screen.getByRole('dialog', { name: new RegExp(label.split(' ')[0]) })).toBeTruthy();
    expect(screen.getByLabelText(/type .* to confirm/i)).toBeTruthy();
    expect((within(document.querySelector<HTMLElement>('.mutation-dialog')!).getByRole('button', { name: label === 'Remote lock' ? 'Lock device' : label }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(screen.getByLabelText(/type .* to confirm/i), { target: { value: phrase } });
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    expect((within(document.querySelector<HTMLElement>('.mutation-dialog')!).getByRole('button', { name: label === 'Remote lock' ? 'Lock device' : label }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('requires review but no phrase for Sync and submits only the selected typed action', async () => {
    apiMock.mockResolvedValue({ ok: true, json: async () => ({ status: 'queued' }) });
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sync device' }));
    expect(screen.queryByLabelText(/type .* to confirm/i)).toBeNull();
    expect((screen.getByRole('button', { name: 'Sync device' }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    fireEvent.click(screen.getByRole('button', { name: 'Sync device' }));
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
    const confirm = screen.getByRole('button', { name: 'Sync device' });
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
    fireEvent.click(screen.getByRole('button', { name: 'Sync device' }));
    expect((await screen.findByRole('alert')).textContent).toMatch(/could not be submitted/i);
    expect(screen.getByRole('alert').textContent).not.toContain('raw transport detail');
    expect((screen.getByRole('button', { name: 'Sync device' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps wrong-case and partial-whitespace phrases disabled', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('button', { name: 'Wipe device' }));
    const phraseInput = screen.getByLabelText(/type .* to confirm/i);
    fireEvent.change(phraseInput, { target: { value: ' wipe ' } });
    fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
    expect((within(document.querySelector<HTMLElement>('.mutation-dialog')!).getByRole('button', { name: 'Wipe device' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('labels the destructive confirmation with the exact action phrase and omits phrase input for Sync', async () => {
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }, { capability: 'devices.privileged.manage', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={loadDevices} />);
    await screen.findByText('WIN-TEST-01');
    fireEvent.click(screen.getByRole('link', { name: 'WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('button', { name: 'Wipe device' }));
    expect(screen.getByLabelText('Type WIPE to confirm')).toBeTruthy();
    expect(screen.getByText('Type WIPE to confirm')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getByRole('button', { name: 'Actions for WIN-TEST-01' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sync device' }));
    expect(screen.queryByLabelText(/type .* to confirm/i)).toBeNull();
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

  it('keeps ids out of rows, shows relative check-in with absolute title, and falls back to Unnamed device', async () => {
    const id = '3f2b8c1e-1111-2222-3333-444455556666';
    render(<DevicesPage capabilities={[{ capability: 'devices.view', state: 'allowed', reasonCode: 'active_role' }]} loadDevices={async () => ({ ...devices, items: [{ ...devices.items[0], id, deviceName: '', complianceState: 'noncompliant', lastSyncDateTime: new Date(Date.now() - 3 * 3600_000).toISOString() }] })} />);
    const link = await screen.findByRole('link', { name: 'Unnamed device' });
    const row = link.closest('tr')!;
    expect(row.textContent).not.toContain(id);
    expect(row.querySelector('.status-badge')!.textContent).toContain('noncompliant');
    expect(row.querySelector('td[title]')!.textContent).toMatch(/ago|hour/);
  });
});
