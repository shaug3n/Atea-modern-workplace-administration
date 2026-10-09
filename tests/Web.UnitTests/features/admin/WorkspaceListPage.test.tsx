import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceListPage } from '../../../../src/Web/src/features/admin/WorkspaceListPage';

const api = vi.hoisted(() => ({ listWorkspaces: vi.fn(), onboardWorkspace: vi.fn() }));
vi.mock('../../../../src/Web/src/features/admin/adminApi', () => ({ adminApi: api }));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('WorkspaceListPage', () => {
  it('shows loading, empty, and API error states', async () => {
    let resolve!: (value: never[]) => void;
    api.listWorkspaces.mockReturnValueOnce(new Promise((r) => { resolve = r; }));
    render(<WorkspaceListPage onOpenWorkspace={vi.fn()} />);
    expect(screen.getByRole('status').textContent).toContain('Loading workspaces');
    resolve([]);
    expect(await screen.findByText('No workspaces are available.')).toBeTruthy();

    api.listWorkspaces.mockRejectedValueOnce(new Error('Your current admin scope cannot view these workspaces.'));
    render(<WorkspaceListPage onOpenWorkspace={vi.fn()} />);
    expect((await screen.findByRole('alert')).textContent).toContain('Your current admin scope cannot view these workspaces.');
  });

  it('validates tenant domain or ID and display name before creating and opens the created workspace', async () => {
    const onOpen = vi.fn();
    api.listWorkspaces.mockResolvedValueOnce([]);
    api.onboardWorkspace.mockResolvedValueOnce({ workspace: { id: 'w-1', tenantId: '11111111-1111-1111-1111-111111111111', displayName: 'Demo', connectionStatus: 'awaiting_invitation' }, invitationUrl: 'http://localhost/invitations/token', expiresAt: '2026-10-01T00:00:00Z' });
    render(<WorkspaceListPage onOpenWorkspace={onOpen} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Create workspace' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));
    expect(screen.getByRole('alert').textContent).toContain('Enter a tenant domain or ID.');
    fireEvent.change(screen.getByLabelText('Tenant domain or ID'), { target: { value: '11111111-1111-1111-1111-111111111111' } });
    fireEvent.change(screen.getByLabelText('Workspace name'), { target: { value: 'Demo' } });
    fireEvent.change(screen.getByLabelText('First admin sign-in address'), { target: { value: 'admin@example.com' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create workspace and invite admin' }));
    expect(await screen.findByText('Demo')).toBeTruthy();
    expect(onOpen).toHaveBeenCalledWith('w-1', expect.objectContaining({ invitationUrl: 'http://localhost/invitations/token' }));
  });

  it('shows a truncated tenant id with the full id available to assistive technology', async () => {
    api.listWorkspaces.mockResolvedValueOnce([{ id: 'w-1', tenantId: '11111111-2222-3333-4444-555555555555', displayName: 'Demo', connectionStatus: 'connected' }]);
    render(<WorkspaceListPage onOpenWorkspace={vi.fn()} />);
    expect(await screen.findByText('11111111…5555')).toBeTruthy();
    expect(document.querySelector('.sr-only')?.textContent).toBe('11111111-2222-3333-4444-555555555555');
    expect(screen.getByRole('button', { name: /Demo/ })).toBeTruthy();
  });
});
