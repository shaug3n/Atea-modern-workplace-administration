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
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Lost authenticator  ' } });
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'RESET MFA' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Reset MFA' }));

    await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/users/user-1/authentication-methods/reset-mfa', expect.objectContaining({ method: 'POST', body: '{"reason":"Lost authenticator"}' })));
  });

  it('explains missing authentication-method consent without making a Graph request', () => {
    render(<AuthenticationMethodsSection userId="user-1" userLabel="Ada Lovelace" decision={consentRequired} />);

    expect(screen.getByRole('status').textContent).toContain('Delegated Microsoft Graph consent is required before this action can run.');
    expect(screen.getByText('UserAuthenticationMethod.Read.All')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Grant delegated consent' })).toBeTruthy();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it.each([
    ['read_only', 'This action is read-only for your current Entra role.'],
    ['consent_required', 'Delegated Microsoft Graph consent is required before this action can run.'],
    ['pim_activation_required', 'Activate the required Entra role in PIM before continuing.'],
    ['pim_approval_required', 'This action is waiting for PIM approval.'],
    ['pim_mfa_required', 'Complete MFA for PIM activation before continuing.'],
    ['pim_eligibility_expired', 'Your PIM eligibility has expired. Request renewed access.'],
    ['disabled', 'This action is disabled for the current workspace.'],
    ['temporarily_unavailable', 'Microsoft Graph authorization could not be verified. Try again later.'],
  ] as const)('shows authentication-management actions as disabled for %s without dispatch', async (state, reason) => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={{
      capability: 'authentication.methods.manage', state, reasonCode: state,
    }} />);

    const tap = await screen.findByRole('button', { name: 'Grant Temporary Access Pass' }) as HTMLButtonElement;
    const remove = screen.getByRole('button', { name: 'Remove' }) as HTMLButtonElement;
    const reset = screen.getByRole('button', { name: 'Reset MFA methods' }) as HTMLButtonElement;
    expect(tap.disabled).toBe(true);
    expect(remove.disabled).toBe(true);
    expect(reset.disabled).toBe(true);
    expect(document.body.textContent).toContain(reason);
    fireEvent.click(tap);
    fireEvent.click(remove);
    fireEvent.click(reset);
    expect(apiMock.mock.calls.every(([, init]) => init?.method !== 'POST' && init?.method !== 'DELETE')).toBe(true);
  });

  it('suppresses authentication-management controls when their capability is hidden', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={{
      capability: 'authentication.methods.manage', state: 'hidden', reasonCode: 'not_returned',
    }} />);

    await screen.findByText('YubiKey');
    expect(screen.queryByRole('button', { name: 'Grant Temporary Access Pass' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Reset MFA methods' })).toBeNull();
  });

  it('reveals a temporary access pass only on the first successful response', async () => {
    apiMock.mockImplementation(async (path: string) => path.includes('temporary-access-pass')
      ? new Response(JSON.stringify({ status: 'succeeded', temporaryAccessPass: 'fixture-tap-value', auditWarning: 'Pass issued, but its audit record could not be written.' }), { status: 200 })
      : new Response(JSON.stringify({ userObjectId: 'user-1', items: [], fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' } }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" userLabel="Ada Lovelace" decision={allowed} manageDecision={manage} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Grant Temporary Access Pass' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  New device access  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Issue Temporary Access Pass' }));

    expect(await screen.findByText('fixture-tap-value')).toBeTruthy();
    expect((await screen.findAllByRole('alert')).some((alert) => alert.textContent?.includes('Pass issued, but its audit record could not be written.'))).toBe(true);
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

  it('reports its single read to the profile summary without fetching separately', async () => {
    apiMock.mockResolvedValue(new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    const onResult = vi.fn();
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} onResult={onResult} />);

    await screen.findByText('YubiKey');
    expect(apiMock).toHaveBeenCalledTimes(1);
    expect(onResult).toHaveBeenLastCalledWith({ status: 'available', items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }] });
  });

  it('retries an unavailable request inside its own busy region', async () => {
    apiMock.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(new Response(JSON.stringify({
      userObjectId: 'user-1',
      items: [],
      fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
    }), { status: 200 }));
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Retry authentication methods' }));
    expect(await screen.findByText('No authentication methods were returned.')).toBeTruthy();
    expect(apiMock).toHaveBeenCalledTimes(2);
  });

  it('invalidates methods after a successful removal when refresh fails and retries only the read', async () => {
    apiMock
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
        fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'succeeded' }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{}', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-2', type: 'phoneAuthenticationMethod', displayName: 'Work phone' }],
        fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }), { status: 200 }));
    const onResult = vi.fn();
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={manage} onResult={onResult} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Device retired  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Remove method' }));

    expect(await screen.findByRole('button', { name: 'Retry authentication methods' })).toBeTruthy();
    expect(screen.queryByText('YubiKey')).toBeNull();
    expect(onResult).toHaveBeenLastCalledWith({ status: 'unavailable', items: [] });

    fireEvent.click(screen.getByRole('button', { name: 'Retry authentication methods' }));
    expect(await screen.findByText('Work phone')).toBeTruthy();
    expect(screen.queryByText('YubiKey')).toBeNull();
    const removeRequest = apiMock.mock.calls.find(([, init]) => init?.method === 'DELETE')!;
    expect(apiMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')).toHaveLength(1);
    expect(removeRequest[0]).toBe('/api/users/user-1/authentication-methods/method-1?type=fido2AuthenticationMethod');
    expect(removeRequest[1].body).toBe('{"reason":"Device retired"}');
    expect(removeRequest[1].headers['Idempotency-Key']).toBeTruthy();
    expect(onResult).toHaveBeenLastCalledWith({ status: 'available', items: [{ id: 'method-2', type: 'phoneAuthenticationMethod', displayName: 'Work phone' }] });
  });

  it('invalidates methods after a successful reset when refresh fails and retries only the read', async () => {
    apiMock
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
        fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'succeeded', removedCount: 1 }), { status: 200 }))
      .mockResolvedValueOnce(new Response('{}', { status: 503 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-2', type: 'phoneAuthenticationMethod', displayName: 'Work phone' }],
        fetchedAt: '2026-09-23T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }), { status: 200 }));
    const onResult = vi.fn();
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={manage} onResult={onResult} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Reset MFA methods' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  User changed devices  ' } });
    fireEvent.change(screen.getByLabelText('Type DISABLE to confirm'), { target: { value: 'RESET MFA' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Reset MFA' }));

    expect(await screen.findByRole('button', { name: 'Retry authentication methods' })).toBeTruthy();
    expect(screen.queryByText('YubiKey')).toBeNull();
    expect(onResult).toHaveBeenLastCalledWith({ status: 'unavailable', items: [] });

    fireEvent.click(screen.getByRole('button', { name: 'Retry authentication methods' }));
    expect(await screen.findByText('Work phone')).toBeTruthy();
    expect(apiMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1);
    const resetRequest = apiMock.mock.calls.find(([, init]) => init?.method === 'POST' && init?.body)!;
    expect(resetRequest[1].body).toBe('{"reason":"User changed devices"}');
    expect(resetRequest[1].headers['Idempotency-Key']).toBeTruthy();
    expect(onResult).toHaveBeenLastCalledWith({ status: 'available', items: [{ id: 'method-2', type: 'phoneAuthenticationMethod', displayName: 'Work phone' }] });
  });

  it('keeps an audit warning visible after a successful security write', async () => {
    apiMock.mockImplementation(async (path: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') return new Response(JSON.stringify({ status: 'succeeded', auditWarning: 'Audit record could not be written.' }), { status: 200 });
      return new Response(JSON.stringify({
        userObjectId: 'user-1',
        items: [{ id: 'method-1', type: 'fido2AuthenticationMethod', displayName: 'YubiKey' }],
        fetchedAt: '2026-09-22T08:00:00Z', freshness: 'live', partialData: false, access: { state: 'allowed' },
      }), { status: 200 });
    });
    render(<AuthenticationMethodsSection userId="user-1" decision={allowed} manageDecision={manage} />);

    fireEvent.click(await screen.findByRole('button', { name: 'Remove' }));
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: '  Credential no longer used  ' } });
    fireEvent.click(screen.getByLabelText('I reviewed the target, change and required capability.'));
    fireEvent.click(screen.getByRole('button', { name: 'Remove method' }));

    expect((await screen.findByRole('alert')).textContent).toContain('Audit record could not be written.');
    const removeRequest = apiMock.mock.calls.find(([, init]) => init?.method === 'DELETE');
    expect(removeRequest?.[1].body).toBe('{"reason":"Credential no longer used"}');
  });
});
