import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { OverviewPage } from '../../src/Web/src/features/overview/OverviewPage';

describe('overview and settings browser states', () => {
  afterEach(cleanup);

  it('renders live overview metrics and PIM attention', async () => {
    render(<OverviewPage loadOverview={async () => ({
      freshness: 'live', fetchedAt: '2026-09-21T10:00:00Z', totalUsers: 120, licenseCoverage: { assigned: 90, available: 30, percentage: 75 }, permissionHealth: { state: 'healthy', allowedCount: 4, totalCount: 6 }, pimAttention: { requiresAttention: true, count: 1 }, partialData: false, access: { state: 'allowed' }
    })} />);
    await waitFor(() => expect(screen.getByText('120')).toBeTruthy());
    expect(screen.getByText('PIM attention needed')).toBeTruthy();
  });

  it('shows retryable unavailable overview state on mobile-sized render', async () => {
    render(<OverviewPage loadOverview={async () => { throw new Error('unavailable'); }} />);
    expect((await screen.findByRole('alert')).textContent).toContain('Overview data is unavailable');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy();
  });
});
