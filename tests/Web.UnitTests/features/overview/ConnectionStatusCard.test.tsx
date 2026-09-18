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
    render(<ConnectionStatusCard state={state as never} />);
    expect(screen.getByTestId('connection-state').textContent).toBe(label);
  });
});
