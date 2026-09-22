import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssociatedDevicesSection } from '../../../../src/Web/src/features/users/AssociatedDevicesSection';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const allowed = { capability: 'devices.view' as const, state: 'allowed' as const, reasonCode: 'active_role' };
const consentRequired = { capability: 'devices.view' as const, state: 'consent_required' as const, reasonCode: 'delegated_scope_required' };

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
});
