import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { LoadingSkeleton } from '../../../src/Web/src/components/LoadingSkeleton';

describe('LoadingSkeleton', () => {
  afterEach(cleanup);

  it('announces loading while hiding decorative placeholders', () => {
    const { container } = render(<LoadingSkeleton label="Loading users" lines={3} />);

    expect(screen.getByRole('status').textContent).toContain('Loading users');
    expect(container.querySelectorAll('.loading-skeleton__line')).toHaveLength(3);
    expect(container.querySelector('.loading-skeleton__placeholders')?.getAttribute('aria-hidden')).toBe('true');
  });
});
