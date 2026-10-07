import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { ActionGroup } from '../../../src/Web/src/components/ActionGroup';

describe('ActionGroup', () => {
  afterEach(cleanup);
  it('defaults a danger group to the Danger zone title', () => {
    render(<ActionGroup tone="danger"><button type="button">Wipe</button></ActionGroup>);
    expect(screen.getByRole('heading', { name: 'Danger zone', level: 3 })).toBeTruthy();
  });
  it('renders a titled default group', () => {
    render(<ActionGroup title="Device actions" description="Routine"><button type="button">Sync</button></ActionGroup>);
    expect(screen.getByRole('heading', { name: 'Device actions' })).toBeTruthy();
  });
});
