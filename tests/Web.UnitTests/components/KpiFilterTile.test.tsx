import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { KpiFilterTile } from '../../../src/Web/src/components/KpiFilterTile';

describe('KpiFilterTile', () => {
  afterEach(cleanup);

  it('keeps_the_button_mode_contract_unchanged', () => {
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

  it('renders_a_native_href_mode_link_with_value_and_destination_and_no_pressed_state', () => {
    render(<KpiFilterTile label="Users" value={42} href="/users" linkLabel="Users: 42, open Users page" />);

    const link = screen.getByRole('link', { name: 'Users: 42, open Users page' });
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('/users');
    expect(link.textContent).toContain('42');
    expect(link.getAttribute('aria-pressed')).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('forwards_native_link_clicks_to_the_optional_callback', () => {
    const onClick = vi.fn((event: React.MouseEvent<HTMLAnchorElement>) => event.preventDefault());
    render(<KpiFilterTile label="Users" value={42} href="/users" linkLabel="Users: 42, open Users page" onClick={onClick} />);
    const link = screen.getByRole('link', { name: 'Users: 42, open Users page' });

    expect(fireEvent.click(link, { button: 0 })).toBe(false);
    expect(onClick).toHaveBeenCalledOnce();
  });
});
