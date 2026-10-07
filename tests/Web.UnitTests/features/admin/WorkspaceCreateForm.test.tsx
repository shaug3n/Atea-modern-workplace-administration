import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceCreateForm } from '../../../../src/Web/src/features/admin/WorkspaceCreateForm';

afterEach(cleanup);
describe('WorkspaceCreateForm', () => {
  it('rejects a blank tenant input', () => {
    const onSubmit = vi.fn();
    render(<WorkspaceCreateForm onSubmit={onSubmit} onCancel={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));
    expect(screen.getByRole('alert').textContent).toContain('Enter a tenant domain or ID.');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submits a tenant domain as tenantDomain, not tenantId', () => {
    const onSubmit = vi.fn().mockResolvedValue({});
    render(<WorkspaceCreateForm onSubmit={onSubmit} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Tenant domain or ID'), { target: { value: 'contoso.com' } });
    fireEvent.change(screen.getByLabelText('Workspace name'), { target: { value: 'Contoso' } });
    fireEvent.change(screen.getByLabelText('First admin sign-in address'), { target: { value: 'admin@contoso.com' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tenantDomain: 'contoso.com' }));
    expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('tenantId');
  });

  it('keeps a GUID in tenantId', () => {
    const onSubmit = vi.fn().mockResolvedValue({});
    render(<WorkspaceCreateForm onSubmit={onSubmit} onCancel={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Tenant domain or ID'), { target: { value: '11111111-1111-1111-1111-111111111111' } });
    fireEvent.change(screen.getByLabelText('Workspace name'), { target: { value: 'Contoso' } });
    fireEvent.change(screen.getByLabelText('First admin sign-in address'), { target: { value: 'admin@contoso.com' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tenantId: '11111111-1111-1111-1111-111111111111' }));
    expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('tenantDomain');
  });
});
