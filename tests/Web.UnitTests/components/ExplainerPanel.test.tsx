import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { ExplainerPanel } from '../../../src/Web/src/components/ExplainerPanel';

describe('ExplainerPanel', () => {
  afterEach(cleanup);

  it('labels the explainer section', () => {
    render(<ExplainerPanel title="Why this matters" id="why-this-matters">Details</ExplainerPanel>);

    const section = screen.getByRole('region', { name: 'Why this matters' });
    expect(section.id).toBe('why-this-matters');
    expect(screen.getByText('Details')).toBeTruthy();
  });
});
