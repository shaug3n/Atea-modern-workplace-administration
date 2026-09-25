import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';

const overview = {
  freshness: 'live', fetchedAt: '2026-09-25T08:00:00Z', totalUsers: 42,
  licenseCoverage: { assigned: 30, available: 12, percentage: 71 },
  permissionHealth: { state: 'allowed', allowedCount: 3, totalCount: 4 },
  pimAttention: { requiresAttention: true, count: 2 }, partialData: false,
  access: { state: 'allowed' }, totalDevices: 1, deviceAccess: { state: 'allowed' },
};

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

  it('does not present a device search page count as the tenant total', async () => {
    render(<OverviewPage loadOverview={async () => overview} session={{ user: {}, workspace: { id: 'w', name: 'Customer', moduleAccess: ['devices'] } }} />);
    await waitFor(() => expect(screen.getByText('Devices')).toBeTruthy());
    expect(screen.getByText('Devices').closest('article')?.textContent).toContain('Unavailable');
    expect(screen.getByText('Devices').closest('article')?.textContent).not.toContain('1');
  });

  it('shows connection health while the summary request is still loading', async () => {
    render(<OverviewPage loadOverview={() => new Promise(() => {})} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: new Date().toISOString() })} />);
    await waitFor(() => expect(screen.getByTestId('connection-state').textContent).toBe('Connected'));
    expect(screen.getByText('Loading overview…')).toBeTruthy();
  });

  it('shows a verified permission count from the API healthy state', async () => {
    render(<OverviewPage loadOverview={async () => ({ ...overview, permissionHealth: { state: 'healthy', allowedCount: 3, totalCount: 4 } })} />);
    await waitFor(() => expect(screen.getByText('3/4')).toBeTruthy());
  });

  it('calls out incomplete permission health in the attention list', async () => {
    render(<OverviewPage loadOverview={async () => ({
      ...overview,
      pimAttention: { requiresAttention: false, count: 0 },
      permissionHealth: { state: 'incomplete', allowedCount: 3, totalCount: 4 },
    })} />);
    await waitFor(() => expect(screen.getByText('3/4')).toBeTruthy());
    expect(screen.getByText('Workspace permissions need attention (3 of 4 available).')).toBeTruthy();
    expect(screen.queryByText('No issues need attention right now.')).toBeNull();
  });
});
