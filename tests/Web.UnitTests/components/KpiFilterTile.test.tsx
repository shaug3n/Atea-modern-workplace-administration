import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { KpiFilterTile } from '../../../src/Web/src/components/KpiFilterTile';

describe('KpiFilterTile', () => {
  afterEach(cleanup);

  it('uses a controlled native button and aria-pressed', () => {
    const onClick = vi.fn();
    const { rerender } = render(<KpiFilterTile label="Users" value={42} detail="Active" selected={false} onClick={onClick} />);
    const button = screen.getByRole('button', { name: 'Users 42 Active' });

    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(button.querySelector('svg[aria-hidden="true"]')).toBeNull();
    expect(screen.getByText('42')).toBeTruthy();
    expect(screen.queryByRole('link')).toBeNull();

    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledOnce();
    expect(button.getAttribute('aria-pressed')).toBe('false');

    rerender(<KpiFilterTile label="Users" value={42} detail="Active" selected onClick={onClick} />);
    const selectedButton = screen.getByRole('button', { name: 'Users 42 Active' });
    expect(selectedButton.getAttribute('aria-pressed')).toBe('true');
    expect(selectedButton.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
  });
});
