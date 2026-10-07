import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PimGuidancePage } from '../../../../src/Web/src/features/identity/PimGuidancePage';
import { NotFoundPage } from '../../../../src/Web/src/components/NotFoundPage';

afterEach(cleanup);

describe('PIM guidance', () => {
  it('opens Entra safely in a new tab and refreshes access once', async () => {
    const refresh = vi.fn(async () => {});
    render(<PimGuidancePage onRefreshAccess={refresh} />);
    const link = screen.getByRole('link', { name: /Open Microsoft Entra PIM/ });
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toContain('noopener');
    expect(link.querySelector('.sr-only')?.textContent).toContain('opens in a new tab');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh access' }));
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Access check refreshed'));
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});

describe('404 page', () => {
  it('shows the attempted path and a way back', () => {
    const go = vi.fn();
    render(<NotFoundPage path="/nope" onNavigate={go} />);
    expect(screen.getByText('Error 404')).toBeTruthy();
    expect(screen.getByRole('heading', { level: 1, name: "This page isn't available" })).toBeTruthy();
    expect(document.querySelector('code')?.textContent).toBe('/nope');
    fireEvent.click(screen.getByRole('link', { name: 'Go to overview' }));
    expect(go).toHaveBeenCalledWith('/overview');
    expect(document.body.textContent).not.toContain('Preview route');
  });
});
