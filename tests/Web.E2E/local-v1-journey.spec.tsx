import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../src/Web/src/app/App';

afterEach(() => { cleanup(); vi.restoreAllMocks(); window.history.replaceState({}, '', '/'); });

describe('customer overview route', () => {
  it('renders the customer overview connection state from route loaders', async () => {
    window.history.replaceState({}, '', '/overview');
    render(<App
      loadSession={async () => ({ user: { displayName: 'Customer admin' }, workspace: { id: 'workspace-1', name: 'Local customer' } })}
      loadCapabilities={async () => ({ evaluatedAt: '2026-09-21T12:00:00Z', sourceState: 'connected', capabilities: [
        { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
        { capability: 'pim.activate', state: 'pim_eligible_inactive', reasonCode: 'eligible_inactive', pim: { state: 'eligible_inactive', activationUrl: 'https://entra.microsoft.com/pim' }, nextStep: { label: 'Activate the required Entra role', href: 'https://entra.microsoft.com/pim' } }
      ] })}
      loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: '2026-09-21T12:00:00Z' })}
    />);

    expect((await screen.findByTestId('connection-state')).textContent).toMatch(/stale|connected/i);
  });
});
