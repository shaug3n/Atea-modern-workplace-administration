import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConsentCallbackPage } from '../../../../src/Web/src/features/workspace-settings/ConsentCallbackPage';

describe('ConsentCallbackPage', () => {
  afterEach(() => { cleanup(); window.history.replaceState(null, '', '/'); });

  it('checks the connection after valid consent and shows the resulting state', async () => {
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=signed&tenant=tenant-1');
    const check = vi.fn().mockResolvedValue({ status: 'connected' });
    render(<ConsentCallbackPage api={{ complete: vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' }), check }} />);
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Consent was received'));
    expect(check).toHaveBeenCalledOnce();
    expect(screen.getByText('Connected')).toBeTruthy();
  });

  it('explains denied consent without exposing the provider payload', async () => {
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=signed&tenant=tenant-1&error=access_denied&error_description=secret-token');
    const check = vi.fn().mockResolvedValue({ status: 'consent_required' });
    render(<ConsentCallbackPage api={{ complete: vi.fn().mockResolvedValue({ valid: true, status: 'consent_denied' }), check }} />);
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Consent was not granted'));
    expect(screen.queryByText('secret-token')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(check).toHaveBeenCalledOnce());
  });

  it('offers retry when the connection check fails', async () => {
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=signed&tenant=tenant-1');
    const check = vi.fn().mockRejectedValue(new Error('provider payload with access_token'));
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' });
    render(<ConsentCallbackPage api={{ complete, check }} />);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy());
    expect(screen.getByRole('alert').textContent).toContain('could not be verified');
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(check).toHaveBeenCalledTimes(2));
    expect(complete).toHaveBeenCalledOnce();
  });

  it('does not send the provider error description to the API', async () => {
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=signed&tenant=tenant-1&error=access_denied&error_description=secret-token');
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_denied' });
    render(<ConsentCallbackPage api={{ complete, check: vi.fn() }} />);
    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy());
    expect(complete).toHaveBeenCalledWith({ state: 'signed', tenant: 'tenant-1', errorCode: 'access_denied' });
  });
});
