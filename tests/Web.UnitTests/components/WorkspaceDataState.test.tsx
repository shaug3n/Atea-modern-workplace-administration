import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceDataState } from '../../../src/Web/src/components/WorkspaceDataState';

describe('WorkspaceDataState', () => {
  afterEach(cleanup);
  it('distinguishes an unavailable data region from an empty result and offers retry', () => {
    const retry = vi.fn();
    render(<WorkspaceDataState state="unavailable" message="Data cannot be shown right now." onRetry={retry} />);
    expect(screen.getByRole('alert').textContent).toContain('Data cannot be shown right now.');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(retry).toHaveBeenCalledOnce();
  });

  it('announces loading and empty states without an error alert', () => {
    const { rerender } = render(<WorkspaceDataState state="loading" message="Loading records…" />);
    expect(screen.getByRole('status').textContent).toContain('Loading records…');
    rerender(<WorkspaceDataState state="empty" message="No records match." />);
    expect(screen.getByRole('status').textContent).toContain('No records match.');
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('lists affected parts for a partial state and exposes a single next step link', () => {
    render(<WorkspaceDataState kind="partial" title="Some sections couldn't load" message="Showing what we could load." affected={['Roles and PIM', 'Associated devices']} />);
    expect(screen.getByRole('status').textContent).toContain("Some sections couldn't load");
    expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Roles and PIM', 'Associated devices']);
    cleanup();
    render(<WorkspaceDataState kind="permission" message="Access needed." action={{ label: 'Open setup', href: '/settings#connection' }} />);
    expect(screen.getByRole('status')).not.toBeNull();
    expect(screen.getByRole('link', { name: 'Open setup' }).getAttribute('href')).toBe('/settings#connection');
  });

  it('disables retry while retrying, marks loading as busy and supports the compact variant', () => {
    const { container } = render(<WorkspaceDataState kind="unavailable" message="Nope." onRetry={() => undefined} retrying compact />);
    const button = screen.getByRole('button', { name: 'Retrying…' }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(container.querySelector('.workspace-data-state--compact')).not.toBeNull();
    cleanup();
    render(<WorkspaceDataState kind="loading" message="Loading…" />);
    expect(screen.getByRole('status').getAttribute('aria-busy')).toBe('true');
  });
});
