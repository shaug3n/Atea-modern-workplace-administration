import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';

describe('OverviewPage', () => {
  afterEach(cleanup);

  it('renders the state returned by the connection-health loader', async () => {
    render(<OverviewPage loadConnectionHealth={async () => ({ status: 'permission_incomplete', lastVerifiedAt: null })} />);
    await waitFor(() => expect(screen.getByTestId('connection-state').textContent).toBe('Permissions incomplete'));
  });

  it('calls the consent API boundary and exposes the returned descriptor', async () => {
    render(<OverviewPage
      loadConnectionHealth={async () => ({ status: 'consent_required', lastVerifiedAt: null })}
      actions={{ check: async () => ({ status: 'connected', lastVerifiedAt: new Date().toISOString() }), startConsent: async () => ({ authorizationUrl: 'https://login.example/authorize?state=safe' }) }} />);
    await waitFor(() => expect(screen.getByTestId('connection-state').textContent).toBe('Consent required'));
    fireEvent.click(screen.getByRole('button', { name: 'Start consent' }));
    await waitFor(() => expect(screen.getByRole('link', { name: 'Continue consent' }).getAttribute('href')).toContain('state=safe'));
  });
});
