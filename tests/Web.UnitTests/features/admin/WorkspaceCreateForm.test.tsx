import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceCreateForm } from '../../../../src/Web/src/features/admin/WorkspaceCreateForm';

afterEach(cleanup);
describe('WorkspaceCreateForm', () => {
  it('rejects blank display names and malformed tenant GUIDs', () => {
    const onSubmit = vi.fn();
    render(<WorkspaceCreateForm onSubmit={onSubmit} onCancel={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));
    expect(screen.getByRole('alert').textContent).toContain('Enter a valid tenant ID.');
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
