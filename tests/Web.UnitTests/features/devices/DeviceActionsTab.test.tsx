import React from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DeviceActionsTab } from '../../../../src/Web/src/features/devices/DeviceActionsTab';
import type { ManagedDevice } from '../../../../src/Web/src/features/devices/devicesApi';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

describe('DeviceActionsTab', () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it('decision_table_covers_only_existing_actions', () => {
    const device: ManagedDevice = { id: 'device-1', deviceName: 'WIN-01', operatingSystem: 'Windows' };
    render(<DeviceActionsTab device={device} capabilities={[{ capability: 'devices.privileged.manage', state: 'allowed' } as never]} />);

    expect(screen.getAllByRole('row')).toHaveLength(6);
    expect(screen.getByRole('table')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Sync device' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Remote lock' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Restart device' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Retire device' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Wipe device' })).toBeTruthy();
    expect(screen.getAllByRole('button')).toHaveLength(5);
    expect(screen.getByRole('table').textContent).toMatch(/platform|management method/i);
    expect(screen.getByRole('table').textContent).toMatch(/re-enroll/i);
    expect(screen.getByRole('table').textContent).toMatch(/personal data/i);
    expect(screen.getByRole('table').textContent).toMatch(/company-managed data/i);
  });

  it('accepted_request_never_claims_device_completion', async () => {
    apiMock.mockResolvedValue({ ok: true, json: async () => ({ status: 'accepted' }) });
    const device: ManagedDevice = { id: 'device-1', deviceName: 'WIN-01', operatingSystem: 'Windows' };
    render(<DeviceActionsTab device={device} capabilities={[{ capability: 'devices.privileged.manage', state: 'allowed' } as never]} />);
    expect(screen.getByText(/Acceptance does not confirm completion\./)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Wipe device' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog.textContent).toContain('Acceptance does not confirm completion.');
    fireEvent.change(screen.getByLabelText(/type wipe to confirm/i), { target: { value: 'WIPE' } });
    fireEvent.click(screen.getByLabelText(/i reviewed/i));
    const confirm = screen.getAllByRole('button', { name: 'Wipe device' }).find(button => button.closest('[role="dialog"]'));
    fireEvent.click(confirm!);
    expect((await screen.findByRole('status')).textContent).toMatch(/acceptance does not confirm completion/i);
    expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/actions/wipe', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'Idempotency-Key': expect.any(String) }),
    }));
  });
});
