import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { StatusPillFilter } from '../../../src/Web/src/components/StatusPillFilter';

describe('StatusPillFilter', () => {
  afterEach(cleanup);

  it('exposes each count and selected state without color dependence', () => {
    const onToggle = vi.fn();
    const options = [
      { value: 'active', label: 'Active', count: 3, selected: true },
      { value: 'paused', label: 'Paused', count: 0, selected: false },
    ] as const;
    const { rerender } = render(<StatusPillFilter options={options} onToggle={onToggle} />);
    const active = screen.getByRole('button', { name: 'Active 3' });
    const paused = screen.getByRole('button', { name: 'Paused 0' });

    expect(active.getAttribute('aria-pressed')).toBe('true');
    expect(paused.getAttribute('aria-pressed')).toBe('false');
    fireEvent.click(paused);
    expect(onToggle).toHaveBeenCalledWith('paused');
    expect(paused.getAttribute('aria-pressed')).toBe('false');

    rerender(<StatusPillFilter options={[options[0], { ...options[1], selected: true }]} onToggle={onToggle} />);
    expect(screen.getByRole('button', { name: 'Paused 0' }).getAttribute('aria-pressed')).toBe('true');
  });
});
