import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthenticationMethodsSection } from '../../../../src/Web/src/features/users/AuthenticationMethodsSection';

const apiMock = vi.hoisted(() => vi.fn());

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => apiMock,
}));

const allowed = { capability: 'authentication.methods.view' as const, state: 'allowed' as const, reasonCode: 'active_role' };
const manage = { capability: 'authentication.methods.manage' as const, state: 'allowed' as const, reasonCode: 'active_role' };
const consentRequired = {
  capability: 'authentication.methods.view' as const,
  state: 'consent_required' as const,
  reasonCode: 'delegated_scope_required',
  missingScopes: ['UserAuthenticationMethod.Read.All'],
  nextStep: { label: 'Grant delegated consent', href: '/api/workspaces/current/consent/start' },
};

describe('AuthenticationMethodsSection', () => {
  afterEach(() => { cleanup(); apiMock.mockReset(); });

  it('offers an explicit reset MFA action and refreshes methods after confirmation', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path.includes('/reset-mfa')) return new Response(JSON.stringify({ status: 'succeeded', removedCount: 1 }), { status: 200 });
      return new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey', createdDateTime: '2026-09-20T08:00:00Z' }],
        fetchedAt: '2026-09-22T08:00:00Z',
        freshness: 'live',
        partialData: false,
        access: { state: 'allowed' },
      }), { status: 200 });
    });

    render(<AuthenticationMethodsSection userId="user-1" userLabel="Ada Lovelace" decision={allowed} manageDecision={manage} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Reset MFA methods' }));
    expect(screen.getByRole('dialog', { name: 'Reset MFA methods' })).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'RESET MFA' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Reset MFA' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/authentication-methods/reset-mfa', expect.objectContaining({ method: 'POST' })));
  });

  it('explains missing authentication-method consent without making a Graph request', () => {
    render(<AuthenticationMethodsSection userId="user-1" userLabel="Ada Lovelace" decision={consentRequired} />);

    expect(screen.getByRole('status').textContent).toContain('Delegated Microsoft Graph consent is required before this action can run.');
    expect(screen.getByText('UserAuthenticationMethod.Read.All')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Grant delegated consent' })).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('reveals a temporary access pass only on the first successful response', async () => {
    apiMock.mockImplementation(async (path: string) => path.includes('temporary-access-pass')
      ? new Response(JSON.stringify({ status: 'succeeded', temporaryAccessPass: 'fixture-tap-value' }), { status: 200 })
      : new Response(JSON.stringify({ userObjectId: 'user-1', items: [], fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" userLabel="Ada Lovelace" decision={allowed} manageDecision={manage} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Temporary Access Pass' }));
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Issue Temporary Access Pass' }));

    expect(await screen.findByText('fixture-tap-value')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(screen.queryByText('fixture-tap-value')).toBeNull();
  });

  it('shows friendly method names, keeps raw types in technical details and puts reset in a danger zone', async () => {
    apiMock.mockImplementation(async () => new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [
        { id: 'm1', type: '#microsoft.graph.fido2AuthenticationMethod', displayName: 'YubiKey', createdDateTime: '2026-09-20T08:00:00Z' },
        { id: 'm2', type: 'somethingNewAuthenticationMethod', displayName: 'Mystery', createdDateTime: '2026-09-20T08:00:00Z' },
      ],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={manage} />);
    expect(await screen.findByText(/Passkey \(FIDO2\)/)).toBeTruthy();
    expect(screen.getByText(/Other method/)).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Danger zone' })).toBeTruthy();
    expect(screen.getByText(/The user must register again/)).toBeTruthy();
  });
});
