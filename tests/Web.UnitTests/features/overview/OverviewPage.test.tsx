import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { OverviewPage } from '../../../../src/Web/src/features/overview/OverviewPage';

describe('OverviewPage', () => {
  afterEach(cleanup);

  it('renders the state returned by the connection-health loader', async () => {
    render(<OverviewPage loadConnectionHealth={async () => ({ status: 'permission_incomplete', lastVerifiedAt: null })} />);
    await waitFor(() => expect(screen.getByTestId('connection-state').textContent).toBe('Permissions incomplete'));
  });
});
