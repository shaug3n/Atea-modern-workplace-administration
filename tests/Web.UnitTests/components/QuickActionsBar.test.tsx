import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { QuickActionsBar } from '../../../src/Web/src/components/QuickActionsBar';

describe('QuickActionsBar', () => {
  afterEach(cleanup);

  it('renders supplied actions with the audit notice', () => {
    render(<QuickActionsBar><button type="button">Refresh</button></QuickActionsBar>);

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeTruthy();
    expect(screen.getByText('All actions are audit-logged')).toBeTruthy();
  });
});
