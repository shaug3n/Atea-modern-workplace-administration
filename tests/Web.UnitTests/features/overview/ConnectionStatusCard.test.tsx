import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { ConnectionStatusCard } from '../../../../src/Web/src/components/ConnectionStatusCard';

describe('ConnectionStatusCard', () => {
  afterEach(cleanup);
  it.each([
    ['awaiting_invitation', 'Invitation pending'], ['consent_required', 'Consent required'], ['connected', 'Connected'],
    ['permission_incomplete', 'Permissions incomplete'], ['temporarily_unavailable', 'Temporarily unavailable'],
    ['consent_revoked', 'Consent revoked'], ['connection_failed', 'Connection failed'],
  ])('renders the explicit %s state', (state, label) => {
    render(<ConnectionStatusCard state={state as never} lastVerifiedAt={state === 'connected' ? new Date().toISOString() : undefined} />);
    expect(screen.getByTestId('connection-state').textContent).toBe(label);
  });

  it('does not present stale connected data as connected', () => {
    render(<ConnectionStatusCard state="connected" lastVerifiedAt="2026-09-18T10:00:00.000Z" now="2026-09-18T10:16:00.000Z" freshnessMs={15 * 60 * 1000} />);
    expect(screen.getByTestId('connection-state').textContent).toBe('Connection check is stale');
    expect(screen.getByTestId('connection-status-card').getAttribute('data-state')).toBe('stale');
  });

  it('treats a missing or malformed verification timestamp as stale', () => {
    render(<ConnectionStatusCard state="connected" lastVerifiedAt="not-a-date" />);
    expect(screen.getByTestId('connection-state').textContent).toBe('Connection check is stale');
  });
});
