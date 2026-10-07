import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DataFreshness } from '../../../src/Web/src/components/DataFreshness';

describe('DataFreshness', () => {
  afterEach(cleanup);
  it('renders a quiet fresh line without alerts or success banners', () => {
    const { container } = render(<DataFreshness fetchedAt={new Date().toISOString()} freshness="fresh" partialData={false} source="Microsoft Graph" />);
    expect(screen.getByText('Up to date')).not.toBeNull();
    expect(container.querySelector('time')?.getAttribute('title')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(container.querySelector('[data-tone="success"]')).toBeNull();
    expect(screen.getByText('Microsoft Graph')).not.toBeNull();
  });

  it('shows a stale label and never renders Invalid Date', () => {
    const { container } = render(<DataFreshness fetchedAt={null} freshness="stale" partialData={false} />);
    expect(screen.getByText('May be out of date')).not.toBeNull();
    expect(container.textContent).toContain('—');
    expect(container.textContent).not.toContain('Invalid');
  });

  it('refreshes on demand and disables the button while refreshing', () => {
    const refresh = vi.fn();
    const { rerender } = render(<DataFreshness fetchedAt="2026-10-07T10:00:00Z" freshness="fresh" partialData={false} onRefresh={refresh} />);
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(refresh).toHaveBeenCalledOnce();
    rerender(<DataFreshness fetchedAt="2026-10-07T10:00:00Z" freshness="fresh" partialData={false} onRefresh={refresh} refreshing />);
    expect((screen.getByRole('button', { name: 'Refreshing…' }) as HTMLButtonElement).disabled).toBe(true);
  });
});
