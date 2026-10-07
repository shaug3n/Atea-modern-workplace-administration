import { cleanup, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { TechnicalDetails } from '../../../src/Web/src/components/TechnicalDetails';

describe('TechnicalDetails', () => {
  afterEach(cleanup);
  it('is collapsed by default and omits items without a value', () => {
    const { container } = render(<TechnicalDetails items={[{ label: 'Device ID', value: 'abc-123' }, { label: 'Serial', value: null }]} />);
    const details = container.querySelector('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(screen.getByText('Technical details')).toBeTruthy();
    expect(screen.getByText('Device ID')).toBeTruthy();
    expect(screen.queryByText('Serial')).toBeNull();
  });

  it('renders nothing when no item has a value', () => {
    const { container } = render(<TechnicalDetails items={[{ label: 'Serial', value: '' }]} />);
    expect(container.firstChild).toBeNull();
  });
});
