import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { StatusBadge } from '../../../src/Web/src/components/StatusBadge';

describe('StatusBadge', () => {
  afterEach(cleanup);
  it('supports the neutral tone with a text label', () => {
    const { container } = render(<StatusBadge tone="neutral" label="Disabled" />);
    expect(screen.getByText('Disabled')).not.toBeNull();
    expect(container.querySelector('[data-tone="neutral"]')).not.toBeNull();
  });

  it('supports compact density while preserving its text label', () => {
    const { container } = render(<StatusBadge density="compact" label="Up to date" />);

    expect(screen.getByText('Up to date')).not.toBeNull();
    expect(container.querySelector('.status-badge--compact')).not.toBeNull();
  });
});
