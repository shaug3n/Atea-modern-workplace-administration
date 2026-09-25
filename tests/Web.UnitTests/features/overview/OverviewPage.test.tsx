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

  it('keeps connection status off the overview while the summary request is loading', async () => {
    render(<OverviewPage loadOverview={() => new Promise(() => {})} loadConnectionHealth={async () => ({ status: 'connected', lastVerifiedAt: new Date().toISOString() })} />);
    expect(screen.getByText('Loading overview…')).toBeTruthy();
    expect(screen.queryByTestId('connection-state')).toBeNull();
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

  it('shows cached counts and the retrieval timestamp when authorized summary data is stale', async () => {
    render(<OverviewPage loadOverview={async () => ({
      ...overview,
      freshness: 'stale',
      partialData: true,
    })} />);

    await waitFor(() => expect(screen.getByText('42')).toBeTruthy());
    expect(screen.getByText(/Freshness: stale/i)).toBeTruthy();
    expect(screen.getByText(new Date(overview.fetchedAt).toLocaleString(), { exact: false })).toBeTruthy();
    expect(screen.queryByText('Entra permission needed')).toBeNull();
  });

  it('describes authorized unavailable summary data without claiming missing permission', async () => {
    render(<OverviewPage loadOverview={async () => ({
      ...overview,
      freshness: 'unavailable',
      partialData: true,
    })} />);

    await waitFor(() => expect(screen.getAllByText('Unavailable').length).toBeGreaterThan(0));
    expect(screen.getAllByText('Summary data temporarily unavailable.').length).toBeGreaterThan(0);
    expect(screen.queryByText('Entra permission needed')).toBeNull();
  });

  it('keeps every metric unavailable when the summary is unavailable despite zero payload values', async () => {
    render(<OverviewPage loadOverview={async () => ({ ...overview, freshness: 'unavailable', partialData: true, totalUsers: 0, licenseCoverage: { assigned: 0, available: 0, percentage: 0 }, permissionHealth: { state: 'healthy', allowedCount: 0, totalCount: 0 } })} />);
    await screen.findByRole('heading', { name: 'Overview' });
    expect(screen.getByText('Users').closest('article')?.textContent).toContain('Unavailable');
    expect(screen.getByText('License coverage').closest('article')?.textContent).toContain('Unavailable');
    expect(screen.getByText('Permission health').closest('article')?.textContent).toContain('Unavailable');
    expect(document.body.textContent).not.toContain('0/0');
  });
});
