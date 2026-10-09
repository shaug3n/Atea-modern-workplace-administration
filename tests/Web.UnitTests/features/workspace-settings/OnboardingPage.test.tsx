import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { readPendingFlow } from '../../../../src/Web/src/features/invitations/pendingFlow';

const api = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => api }));

import { OnboardingPage } from '../../../../src/Web/src/features/workspace-settings/OnboardingPage';

const tenantId = '11111111-1111-1111-1111-111111111111';
const challenge = 'workspace-signed-challenge';
const authorizationUrl = `https://login.microsoftonline.com/${tenantId}/v2.0/adminconsent?client_id=api-client&scope=https%3A%2F%2Fgraph.microsoft.com%2F.default&redirect_uri=${encodeURIComponent(`${window.location.origin}/onboarding/consent/callback`)}&state=${challenge}`;

describe('OnboardingPage consent transaction', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    api.mockImplementation((path: string) => {
      if (path.endsWith('/consent/start')) {
        return Promise.resolve(new Response(JSON.stringify({ authorizationUrl, challenge, correlationId: 'correlation' }), { status: 200 }));
      }
      return Promise.resolve(new Response(JSON.stringify({ status: 'consent_required', lastVerifiedAt: null }), { status: 200 }));
    });
  });
  afterEach(() => { cleanup(); window.sessionStorage.clear(); vi.clearAllMocks(); });

  it('stores the validated workspace transaction before exposing the consent link', async () => {
    render(<OnboardingPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Start consent' }));

    expect(await screen.findByRole('link', { name: 'Continue consent' })).toBeTruthy();
    expect(readPendingFlow()).toMatchObject({
      kind: 'workspace',
      challenge,
      tenantId,
      step: 'workspace_callback',
    });
    expect(readPendingFlow()).not.toHaveProperty('nonce');
  });

  it('rejects an untrusted consent URL without creating a pending transaction', async () => {
    api.mockImplementation((path: string) => path.endsWith('/consent/start')
      ? Promise.resolve(new Response(JSON.stringify({ authorizationUrl: authorizationUrl.replace('login.microsoftonline.com', 'evil.example'), challenge }), { status: 200 }))
      : Promise.resolve(new Response(JSON.stringify({ status: 'consent_required', lastVerifiedAt: null }), { status: 200 })));
    render(<OnboardingPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Start consent' }));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(readPendingFlow()).toBeNull();
  });
});
