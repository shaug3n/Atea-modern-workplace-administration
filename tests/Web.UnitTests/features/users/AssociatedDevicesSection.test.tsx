import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssociatedDevicesSection } from '../../../../src/Web/src/features/users/AssociatedDevicesSection';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const allowed = { capability: 'devices.view' as const, state: 'allowed' as const, reasonCode: 'active_role' };
const consentRequired = { capability: 'devices.view' as const, state: 'consent_required' as const, reasonCode: 'delegated_scope_required' };
const readOnly = { capability: 'devices.view' as const, state: 'read_only' as const, reasonCode: 'role_read_only' };
const pimRequired = { capability: 'devices.view' as const, state: 'pim_activation_required' as const, reasonCode: 'pim_activation_required' };
const disabled = { capability: 'devices.view' as const, state: 'disabled' as const, reasonCode: 'module_disabled' };

describe('AssociatedDevicesSection', () => {
  afterEach(() => { cleanup(); apiMock.mockReset(); });

  it('shows an associated managed device and opens its details link', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      items: [{ id: 'device-1', deviceName: 'WIN-TEST-01', operatingSystem: 'Windows' }],
      fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false,
      access: { state: 'allowed' },
    }), { status: 200 }));

    render(<AssociatedDevicesSection userId="user-1" decision={allowed} />);

    expect(await screen.findByText('WIN-TEST-01')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Open device WIN-TEST-01' }).getAttribute('href')).toBe('/devices?device=device-1');
  });

  it('does not fetch when the capability requires consent', () => {
    render(<AssociatedDevicesSection userId="user-1" decision={consentRequired} />);

    expect(screen.getByRole('status').textContent).toContain('Delegated Microsoft Graph consent is required');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('fetches for read-only access without rendering a mutation affordance', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [], fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'read_only' } }), { status: 200 }));
    render(<AssociatedDevicesSection userId="user-1" decision={readOnly} />);

    expect(await screen.findByText('No managed devices match the current filters.')).toBeTruthy();
    expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/devices');
  });

  it('does not fetch for PIM-gated access', () => {
    render(<AssociatedDevicesSection userId="user-1" decision={pimRequired} />);

    expect(screen.getByRole('status').textContent).toContain('Activate the required Entra role');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('does not fetch for disabled access', () => {
    render(<AssociatedDevicesSection userId="user-1" decision={disabled} />);

    expect(apiMock).not.toHaveBeenCalled();
  });

  it('uses safe unavailable copy when the API returns an error-bearing response', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({ items: [], fetchedAt: '2026-09-23T08:00:00Z', freshness: 'unavailable', partialData: true, access: { state: 'temporarily_unavailable' }, error: { category: 'graph', message: 'secret tenant diagnostic' } }), { status: 200 }));
    render(<AssociatedDevicesSection userId="user-1" decision={allowed} />);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toBe('Managed devices are unavailable. Check delegated permissions and try again.');
    expect(alert.textContent).not.toContain('secret tenant diagnostic');
  });
});
