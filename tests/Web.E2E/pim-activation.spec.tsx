import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RolesAndPimSection } from '../../src/Web/src/features/users/RolesAndPimSection';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

describe('PIM activation browser boundary', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('requests directory-role PIM activation only after explicit confirmation and never calls Graph from the browser', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'activation_pending',
      requiredCapability: 'pim.activate',
      replayed: false,
      requestId: 'request-1',
      handoff: {
        nextStep: 'Wait for PIM approval',
        roleTemplateId: 'e8611ab8-c189-46e8-94e1-60213ab1f814',
        roleDisplayName: 'Privileged Role Administrator',
        portalUrl: 'https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade',
        refreshAction: '/api/capabilities',
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(
      <RolesAndPimSection
        roles={{
          access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false },
          items: [],
        }}
        pim={{
          access: { authorization: { capability: 'pim.activate', state: 'pim_activation_required', reasonCode: 'pim_activation_required' }, fetchedAt: new Date().toISOString(), freshness: 'fresh', partialData: false },
          items: [{
            id: 'eligibility-1',
            roleTemplateId: 'e8611ab8-c189-46e8-94e1-60213ab1f814',
            roleDefinitionId: 'role-definition-id',
            displayName: 'Privileged Role Administrator',
            status: 'eligible_inactive',
            requiredCapability: 'pim.activate',
            activationAvailable: true,
            requiresApproval: true,
            requiresMfa: true,
            requiresJustification: true,
            maximumDurationMinutes: 480,
            directoryScopeId: '/',
            activationAction: { action: 'request_activation', href: '/api/pim/activations', method: 'POST', requiredCapability: 'pim.activate', requiresConfirmation: true },
          }],
        }}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Request activation for Privileged Role Administrator' }));
    expect(screen.getByRole('dialog').querySelector('[aria-label="PIM policy requirements"]')?.textContent).toContain('MFA required');
    const submit = screen.getByRole('button', { name: 'Request activation' }) as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Business justification'), { target: { value: 'Approved support case' } });
    expect(submit.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('I understand this will request activation of my eligible Entra directory role.'));
    expect(submit.disabled).toBe(false);
    fireEvent.click(submit);

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(new URL(path, window.location.origin).origin).toBe(window.location.origin);
    expect(path).toBe('/api/pim/activations');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(JSON.parse(String(init.body))).toMatchObject({ confirmed: true, roleType: 'directoryRole' });
    expect(JSON.parse(String(init.body))).not.toHaveProperty('mfaCompleted');
    expect(await screen.findByText('The activation request is pending.')).toBeTruthy();
    expect(screen.getByText('Wait for PIM approval')).toBeTruthy();
  });
});
