import React from 'react';
import { StrictMode } from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConsentCallbackPage } from '../../src/Web/src/features/workspace-settings/ConsentCallbackPage';
import { readPendingFlow, writePendingFlow } from '../../src/Web/src/features/invitations/pendingFlow';

describe('delegated consent callback browser flow', () => {
  afterEach(() => { cleanup(); window.sessionStorage.clear(); window.history.replaceState(null, '', '/'); });

  it('completes consent, checks the connection, and exposes a safe retry path', async () => {
    const state = 'signed';
    const tenant = '11111111-1111-1111-1111-111111111111';
    window.history.replaceState(null, '', `/onboarding/consent/callback?state=${state}&tenant=${tenant}`);
    writePendingFlow({
      kind: 'workspace',
      challenge: state,
      tenantId: tenant,
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      step: 'workspace_callback',
    });
    expect(readPendingFlow()?.kind).toBe('workspace');
    const check = vi.fn().mockResolvedValue({ status: 'connected', lastVerifiedAt: new Date().toISOString() });
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' });

    render(<StrictMode><ConsentCallbackPage api={{ complete, check }} /></StrictMode>);

    expect(readPendingFlow()?.kind).toBe('workspace');
    await waitFor(() => expect(screen.getByText('Connected')).toBeTruthy());
    expect(complete).toHaveBeenCalledOnce();
    expect(check).toHaveBeenCalledOnce();
    expect(screen.getByRole('link', { name: 'Return to overview' }).getAttribute('href')).toBe('/overview');
    expect(window.location.search).toBe('');
    expect(readPendingFlow()).toBeNull();
  });
});
