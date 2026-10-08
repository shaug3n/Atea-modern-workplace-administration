import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConsentCallbackPage } from '../../../../src/Web/src/features/workspace-settings/ConsentCallbackPage';
import { writePendingFlow } from '../../../../src/Web/src/features/invitations/pendingFlow';

describe('ConsentCallbackPage', () => {
  const tenant = '11111111-1111-1111-1111-111111111111';
  const prepare = (error = '') => {
    const state = `signed-${crypto.randomUUID()}`;
    const query = `state=${state}&tenant=${tenant}${error ? `&error=${error}&error_description=secret-token` : ''}`;
    window.history.replaceState(null, '', `/onboarding/consent/callback?${query}`);
    writePendingFlow({
      kind: 'workspace',
      challenge: state,
      tenantId: tenant,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      step: 'workspace_callback',
    });
    return state;
  };
  afterEach(() => { cleanup(); window.sessionStorage.clear(); window.history.replaceState(null, '', '/'); });

  it('checks the connection after valid consent and shows the resulting state', async () => {
    prepare();
    const check = vi.fn().mockResolvedValue({ status: 'connected', lastVerifiedAt: new Date().toISOString() });
    render(<ConsentCallbackPage api={{ complete: vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' }), check }} />);
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Consent was received'));
    expect(check).toHaveBeenCalledOnce();
    expect(screen.getByText('Connected')).toBeTruthy();
  });

  it('explains denied consent without exposing the provider payload', async () => {
    prepare('access_denied');
    const check = vi.fn().mockResolvedValue({ status: 'consent_required' });
    render(<ConsentCallbackPage api={{ complete: vi.fn().mockResolvedValue({ valid: true, status: 'consent_denied' }), check }} />);
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Consent was not granted'));
    expect(screen.queryByText('secret-token')).toBeNull();
    expect(check).toHaveBeenCalledOnce();
  });

  it('offers retry when the connection check fails', async () => {
    prepare();
    const check = vi.fn().mockRejectedValue(new Error('provider payload with access_token'));
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' });
    render(<ConsentCallbackPage api={{ complete, check }} />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy());
    expect(screen.getByRole('alert').textContent).toContain('could not be verified');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(check).toHaveBeenCalledTimes(2));
    expect(complete).toHaveBeenCalledOnce();
  });

  it('keeps definite missing permissions primary while showing unknown scopes and retry guidance', async () => {
    prepare();
    const check = vi.fn().mockResolvedValue({
      status: 'permission_incomplete',
      permissionCoverage: {
        availableScopes: [],
        missingScopes: ['Directory.Read.All'],
        unknownScopes: ['MailboxSettings.Read'],
      },
    });
    render(<ConsentCallbackPage api={{
      complete: vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' }),
      check,
    }} />);

    expect(await screen.findByText('Directory.Read.All')).toBeTruthy();
    expect(screen.getByRole('link', { name: 'Review permissions and consent' })).toBeTruthy();
    expect(screen.getByText('MailboxSettings.Read')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy();
  });

  it('does not send the provider error description to the API', async () => {
    const state = prepare('access_denied');
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_denied' });
    render(<ConsentCallbackPage api={{ complete, check: vi.fn() }} />);
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect(complete).toHaveBeenCalledWith({ state, tenant, errorCode: 'consent_denied' });
  });
});
