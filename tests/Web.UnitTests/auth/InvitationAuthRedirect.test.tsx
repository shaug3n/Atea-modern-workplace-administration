import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React, { StrictMode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createAuthActions } from '../../../src/Web/src/auth/AuthProvider';
import { writePendingFlow } from '../../../src/Web/src/features/invitations/pendingFlow';

const auth = vi.hoisted(() => ({ signInForTenant: vi.fn() }));
vi.mock('../../../src/Web/src/auth/AuthProvider', async () => {
  const actual = await vi.importActual<typeof import('../../../src/Web/src/auth/AuthProvider')>('../../../src/Web/src/auth/AuthProvider');
  return { ...actual, useAuth: () => ({ signInForTenant: auth.signInForTenant }) };
});

import { InvitationConsentCallbackPage } from '../../../src/Web/src/features/workspace-settings/InvitationConsentCallbackPage';

describe('invitation authentication redirect', () => {
  const nonce = 'A'.repeat(43);
  const challenge = 'signed-challenge';
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const future = () => new Date(Date.now() + 60_000).toISOString();

  afterEach(() => {
    cleanup();
    sessionStorage.clear();
    window.history.replaceState({}, '', '/');
    vi.clearAllMocks();
  });

  it('preserves the invitation return URI with an opaque state value', async () => {
    window.history.pushState({}, '', '/invitations/route-secret?source=email');
    const instance = {
      loginRedirect: vi.fn().mockResolvedValue(undefined),
      logoutRedirect: vi.fn(),
      acquireTokenSilent: vi.fn(),
      acquireTokenRedirect: vi.fn()
    };

    await createAuthActions(instance, { username: 'customer@example.com' } as never, vi.fn()).signIn();

    expect(instance.loginRedirect).toHaveBeenCalledWith({
      scopes: [expect.any(String)],
      redirectStartPage: window.location.href,
      state: expect.stringMatching(/^[a-f0-9-]{36}$/)
    });
    expect(instance.loginRedirect.mock.calls[0][0].state).not.toContain('route-secret');
  });

  it('uses the server tenant authority and refuses a token for an account cached in another tenant', async () => {
    window.history.replaceState({}, '', `/invitations/${nonce}`);
    const instance = {
      loginRedirect: vi.fn().mockResolvedValue(undefined),
      logoutRedirect: vi.fn(),
      acquireTokenSilent: vi.fn(),
      acquireTokenRedirect: vi.fn(),
    };
    const cachedAccount = { tenantId: '22222222-2222-2222-2222-222222222222' };
    const actions = createAuthActions(instance, cachedAccount as never, vi.fn(), [cachedAccount] as never);

    await actions.signInForTenant(tenantId, `/invitations/${nonce}`);
    await expect(actions.getApiToken(tenantId)).rejects.toThrow();

    expect(instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({
      authority: `https://login.microsoftonline.com/${tenantId}`,
      prompt: 'select_account',
    }));
    expect(instance.acquireTokenSilent).not.toHaveBeenCalled();
  });

  it('resumes and starts tenant-pinned sign-in once across StrictMode effects', async () => {
    window.history.replaceState({}, '', `/onboarding/consent/callback?state=${challenge}&tenant=${tenantId}`);
    writePendingFlow({ kind: 'invitation', nonce, challenge, expiresAt: future(), step: 'consent_callback' });
    const resume = vi.fn().mockResolvedValue({ valid: true, status: 'ready_to_sign_in', tenantId, correlationId: 'correlation' });

    render(<StrictMode><InvitationConsentCallbackPage resume={resume} /></StrictMode>);

    await waitFor(() => expect(auth.signInForTenant).toHaveBeenCalledOnce());
    expect(resume).toHaveBeenCalledOnce();
    expect(resume).toHaveBeenCalledWith(nonce, challenge, tenantId, undefined);
    expect(auth.signInForTenant).toHaveBeenCalledWith(tenantId, `/invitations/${nonce}`);
    expect(window.location.search).toBe('');
  });

  it('does not resume or sign in when callback state differs from the tab transaction', async () => {
    window.history.replaceState({}, '', `/onboarding/consent/callback?state=other&tenant=${tenantId}`);
    writePendingFlow({ kind: 'invitation', nonce, challenge, expiresAt: future(), step: 'consent_callback' });
    const resume = vi.fn();

    render(<InvitationConsentCallbackPage resume={resume} />);

    expect(await screen.findByText('Open your original invitation and try again.')).toBeTruthy();
    expect(resume).not.toHaveBeenCalled();
    expect(auth.signInForTenant).not.toHaveBeenCalled();
  });

  it('requires the original tab transaction before processing callback parameters', async () => {
    window.history.replaceState({}, '', `/onboarding/consent/callback?state=${challenge}&tenant=${tenantId}`);
    const resume = vi.fn();

    render(<InvitationConsentCallbackPage resume={resume} />);

    expect(await screen.findByText('Open your original invitation and try again.')).toBeTruthy();
    expect(resume).not.toHaveBeenCalled();
    expect(auth.signInForTenant).not.toHaveBeenCalled();
    expect(window.location.search).toBe('');
  });

  it('does not sign in after consent was denied', async () => {
    window.history.replaceState({}, '', `/onboarding/consent/callback?state=${challenge}&error=access_denied`);
    writePendingFlow({ kind: 'invitation', nonce, challenge, expiresAt: future(), step: 'consent_callback' });
    const resume = vi.fn().mockResolvedValue({ valid: true, status: 'consent_denied', correlationId: 'correlation' });

    render(<InvitationConsentCallbackPage resume={resume} />);

    expect(await screen.findByText(/Consent was not granted/)).toBeTruthy();
    expect(resume).toHaveBeenCalledOnce();
    expect(auth.signInForTenant).not.toHaveBeenCalled();
  });

  it('does not sign in when the server rejects a mismatched callback tenant', async () => {
    const otherTenant = '22222222-2222-2222-2222-222222222222';
    window.history.replaceState({}, '', `/onboarding/consent/callback?state=${challenge}&tenant=${otherTenant}`);
    writePendingFlow({ kind: 'invitation', nonce, challenge, expiresAt: future(), step: 'consent_callback' });
    const resume = vi.fn().mockResolvedValue({ valid: false, status: 'invalid_callback', correlationId: 'correlation' });

    render(<InvitationConsentCallbackPage resume={resume} />);

    expect(await screen.findByText('Open your original invitation and try again.')).toBeTruthy();
    expect(resume).toHaveBeenCalledWith(nonce, challenge, otherTenant, undefined);
    expect(auth.signInForTenant).not.toHaveBeenCalled();
  });
});
