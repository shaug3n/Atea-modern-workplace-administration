import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MetricCard } from '../../../src/Web/src/components/MetricCard';

describe('MetricCard', () => {
  afterEach(cleanup);
  it('is a single named link that navigates through the SPA handler', () => {
    const onNavigate = vi.fn();
    render(<MetricCard label="Users" value={42} href="/users" linkLabel="Users" onNavigate={onNavigate} />);
    const link = screen.getByRole('link', { name: 'Users: 42, open Users' });
    fireEvent.click(link);
    expect(onNavigate).toHaveBeenCalledWith('/users');
  });

  it('shows a dash and a reason instead of the word Unavailable when there is no value', () => {
    render(<MetricCard label="Devices" value={null} unavailableReason="You need access to view this." action={{ label: 'Review access', href: '/settings#connection' }} />);
    expect(screen.getByText('—')).toBeTruthy();
    expect(screen.getByText('You need access to view this.')).toBeTruthy();
    expect(screen.queryByText('Unavailable')).toBeNull();
    expect(screen.getByRole('link', { name: 'Review access' }).getAttribute('href')).toBe('/settings#connection');
  });
});
