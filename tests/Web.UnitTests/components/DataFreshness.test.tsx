import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DataFreshness } from '../../../src/Web/src/components/DataFreshness';

describe('DataFreshness', () => {
  afterEach(cleanup);
  it('renders the legacy quiet fresh line by default', () => {
    const { container } = render(<DataFreshness fetchedAt={new Date().toISOString()} freshness="fresh" partialData={false} source="Microsoft Graph" />);
    expect(screen.getByText('Up to date')).not.toBeNull();
    expect(container.querySelector('time')?.getAttribute('title')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(container.querySelector('[data-tone="success"]')).toBeNull();
    expect(screen.getByText('Microsoft Graph')).not.toBeNull();
  });

  it('renders the compact pill variant while preserving supplied details and refresh', () => {
    const refresh = vi.fn();
    const { container } = render(
      <DataFreshness
        fetchedAt="2026-10-07T10:00:00Z"
        freshness="fresh"
        partialData={false}
        presentation="pill"
        source="Microsoft Graph"
        message="Directory data is current."
        onRefresh={refresh}
      />
    );

    expect(container.querySelector('.data-freshness--pill')).not.toBeNull();
    expect(screen.getByText('Up to date')).not.toBeNull();
    expect(screen.getByText('Directory data is current.')).not.toBeNull();
    expect(screen.getByText('Microsoft Graph')).not.toBeNull();
    expect(container.querySelector('time')?.getAttribute('title')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('shows freshness and partial data independently in a banner', () => {
    const { container } = render(
      <DataFreshness fetchedAt={null} freshness="fresh" partialData presentation="banner" />
    );

    expect(container.querySelector('.data-freshness--banner')).not.toBeNull();
    expect(screen.getByText('Up to date')).not.toBeNull();
    expect(screen.getByText('Partly loaded')).not.toBeNull();
  });

  it('shows stale, partial, and throttled states independently in a banner', () => {
    const { container } = render(
      <DataFreshness fetchedAt={null} freshness="stale" partialData presentation="banner" throttled />
    );

    expect(screen.getByText('May be out of date')).not.toBeNull();
    expect(screen.getByText('Partly loaded')).not.toBeNull();
    expect(screen.getByText('Refresh throttled')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="success"]')).toBeNull();
    expect(container.textContent?.toLowerCase()).not.toContain('retry');
    expect(container.textContent).not.toMatch(/\d+\s*(seconds|minutes)/i);
  });

  it('shows throttling in the line presentation only when opted in', () => {
    const { container, rerender } = render(
      <DataFreshness fetchedAt={null} freshness="fresh" partialData={false} />
    );

    expect(screen.queryByText('Refresh throttled')).toBeNull();
    rerender(<DataFreshness fetchedAt={null} freshness="fresh" partialData={false} throttled />);

    expect(screen.getByText('Refresh throttled')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="warning"]')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="success"]')).toBeNull();
  });

  it('shows unavailable status without a success tone', () => {
    const { container } = render(<DataFreshness fetchedAt={null} freshness="unavailable" partialData={false} presentation="pill" />);

    expect(screen.getByText('Unavailable')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="danger"]')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="success"]')).toBeNull();
  });

  it('shows throttling without success tone or invented retry timing', () => {
    const { container } = render(
      <DataFreshness fetchedAt={null} freshness="fresh" partialData={false} presentation="banner" throttled />
    );

    expect(screen.getByText('Refresh throttled')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="warning"]')).not.toBeNull();
    expect(container.querySelector('.status-badge[data-tone="success"]')).toBeNull();
    expect(container.textContent?.toLowerCase()).not.toContain('retry');
    expect(container.textContent).not.toMatch(/\d+\s*(seconds|minutes)/i);
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
