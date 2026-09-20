import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PimActivationDialog } from '../../../../src/Web/src/features/pim/PimActivationDialog';
import { RolesAndPimSection } from '../../../../src/Web/src/features/users/RolesAndPimSection';
import type { PimEligibility } from '../../../../src/Web/src/features/users/userDetailApi';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const eligible: PimEligibility = {
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
  expiresAt: '2026-09-21T12:00:00Z',
  activationAction: { action: 'request_activation', href: '/api/pim/activations', method: 'POST', requiredCapability: 'pim.activate', requiresConfirmation: true },
};

describe('PimActivationDialog', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
  });

  it('requires explicit confirmation and justification before posting to the BFF with an idempotency key', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'activation_pending',
      requiredCapability: 'pim.activate',
      replayed: false,
      requestId: 'request-1',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(<PimActivationDialog eligibility={eligible} />);

    const submit = screen.getByRole('button', { name: 'Request activation' }) as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    fireEvent.click(screen.getByLabelText('I understand this will request activation of my eligible Entra directory role.'));
    expect(submit.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Business justification'), { target: { value: 'Need to complete approved admin work' } });
    expect(submit.disabled).toBe(false);
    fireEvent.click(submit);

    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(1));
    const [path, init] = apiMock.mock.calls[0];
    expect(path).toBe('/api/pim/activations');
    expect(path).not.toMatch(/graph\.microsoft\.com/i);
    expect(init.headers['Idempotency-Key']).toBeTruthy();
    expect(JSON.parse(String(init.body))).toEqual({
      roleTemplateId: 'e8611ab8-c189-46e8-94e1-60213ab1f814',
      durationMinutes: 60,
      justification: 'Need to complete approved admin work',
      confirmed: true,
      roleType: 'directoryRole',
    });
  });

  it('shows pending status and guided handoff without claiming the role is active', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      status: 'activation_pending',
      requiredCapability: 'pim.activate',
      replayed: false,
      requestId: 'request-1',
      handoff: {
        nextStep: 'Wait for PIM approval',
        roleTemplateId: eligible.roleTemplateId,
        roleDisplayName: 'Privileged Role Administrator',
        portalUrl: 'https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade',
        refreshAction: '/api/capabilities',
        graphCorrelationId: 'corr-1',
      },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    render(<PimActivationDialog eligibility={eligible} />);
    fireEvent.click(screen.getByLabelText('I understand this will request activation of my eligible Entra directory role.'));
    fireEvent.change(screen.getByLabelText('Business justification'), { target: { value: 'Need access' } });
    fireEvent.click(screen.getByRole('button', { name: 'Request activation' }));

    expect(await screen.findByText('The activation request is pending.')).toBeTruthy();
    expect(screen.getByText('Wait for PIM approval')).toBeTruthy();
    expect(screen.getByText('corr-1')).toBeTruthy();
    expect(screen.queryByText('The role is active.')).toBeNull();
  });

  it('wires activation from RolesAndPimSection only for eligible inactive roles', () => {
    render(
      <RolesAndPimSection
        roles={{ access: { authorization: { capability: 'roles.assign', state: 'read_only', reasonCode: 'role_read_only' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false }, items: [] }}
        pim={{ access: { authorization: { capability: 'pim.activate', state: 'pim_activation_required', reasonCode: 'pim_activation_required' }, fetchedAt: '2026-09-21T08:00:00Z', freshness: 'fresh', partialData: false }, items: [eligible, { ...eligible, id: 'active-1', status: 'active', activationAvailable: false, displayName: 'Global Administrator' }] }}
      />,
    );

    const request = screen.getByRole('button', { name: 'Request activation for Privileged Role Administrator' });
    const active = screen.getByRole('button', { name: 'Request activation for Global Administrator' }) as HTMLButtonElement;
    expect(active.disabled).toBe(true);
    fireEvent.click(request);
    expect(screen.getByRole('dialog', { name: 'Request PIM activation' })).toBeTruthy();
  });
});
