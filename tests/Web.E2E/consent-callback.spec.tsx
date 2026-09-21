import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConsentCallbackPage } from '../../src/Web/src/features/workspace-settings/ConsentCallbackPage';

describe('delegated consent callback browser flow', () => {
  afterEach(() => { cleanup(); window.history.replaceState(null, '', '/'); });

  it('completes consent, checks the connection, and exposes a safe retry path', async () => {
    window.history.replaceState(null, '', '/onboarding/consent/callback?state=signed&tenant=tenant-1');
    const check = vi.fn().mockResolvedValue({ status: 'connected' });
    const complete = vi.fn().mockResolvedValue({ valid: true, status: 'consent_received' });

    render(<ConsentCallbackPage api={{ complete, check }} />);

    await waitFor(() => expect(screen.getByText('Connected')).toBeTruthy());
    expect(complete).toHaveBeenCalledOnce();
    expect(check).toHaveBeenCalledOnce();
    expect(screen.getByRole('link', { name: 'Return to overview' }).getAttribute('href')).toBe('/overview');
  });
});
