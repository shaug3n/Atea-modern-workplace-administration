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
});
