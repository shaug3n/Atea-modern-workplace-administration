import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceModulesPage } from '../../../../src/Web/src/features/workspace-settings/WorkspaceModulesPage';
import { WorkspaceAccessPage } from '../../../../src/Web/src/features/workspace-access/WorkspaceAccessPage';

const { api } = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => api }));

describe('Settings data pages', () => {
  afterEach(() => { cleanup(); api.mockReset(); });

  it('keeps the Modules header through load failure and retries to a sourced result', async () => {
    api.mockRejectedValueOnce(new Error('private module error')).mockResolvedValue({ ok: true, json: async () => ({ enabledModules: ['users'] }) });
    render(<WorkspaceModulesPage />);
    expect(screen.getByRole('heading', { name: 'Modules' })).toBeTruthy();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy());
    expect(document.body.textContent).not.toContain('private module error');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Users' })).toBeTruthy());
    expect(screen.getByRole('switch', { name: 'Users' })).toBeTruthy();
  });

  it('lets the owner deliberately enable the license hygiene module', async () => {
    api.mockImplementation(async (_path: string, init?: RequestInit) => ({
      ok: true,
      json: async () => init?.method === 'PATCH'
        ? { enabledModules: ['users', 'license-hygiene'] }
        : { enabledModules: ['users'] },
    }));
    render(<WorkspaceModulesPage />);

    const hygieneSwitch = await screen.findByRole('switch', { name: 'License Hygiene' });
    expect((hygieneSwitch as HTMLInputElement).checked).toBe(false);
    fireEvent.click(hygieneSwitch);
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

    await waitFor(() => expect(api).toHaveBeenCalledWith('/api/workspaces/current/modules', expect.objectContaining({ method: 'PATCH' })));
    const patch = api.mock.calls.find(([, init]) => init?.method === 'PATCH')?.[1];
    expect(JSON.parse(patch?.body as string).enabledModules).toEqual(['users', 'license-hygiene']);
  });

  it('keeps the Access header through load failure and retries to a sourced empty result', async () => {
    api.mockRejectedValueOnce(new Error('private access error')).mockResolvedValue({ ok: true, json: async () => ({ memberships: [], invitations: [] }) });
    render(<WorkspaceAccessPage />);
    expect(screen.getByRole('heading', { name: 'Workspace access' })).toBeTruthy();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy());
    expect(document.body.textContent).not.toContain('private access error');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.getByText('No workspace members have been added yet.')).toBeTruthy());
    expect(screen.getByText('No workspace members have been added yet.')).toBeTruthy();
  });

  it('allows deliberate hygiene assignment only when the module is enabled', async () => {
    api.mockResolvedValue({
      ok: true,
      json: async () => ({
        memberships: [{ id: 'member-1', tenantObjectId: 'entra-1', email: 'member@example.com', platformRole: 'member', moduleKeys: ['users'], createdAt: '2026-01-01T00:00:00Z' }],
        invitations: [],
      }),
    });
    const { rerender } = render(<WorkspaceAccessPage isOwner canManageModules availableModules={['users']} />);
    const inviteHygiene = await screen.findByRole('checkbox', { name: 'License Hygiene' });
    const memberHygiene = screen.getByRole('checkbox', { name: 'License Hygiene for member@example.com' });
    expect(inviteHygiene.hasAttribute('disabled')).toBe(true);
    expect(memberHygiene.hasAttribute('disabled')).toBe(true);

    rerender(<WorkspaceAccessPage isOwner canManageModules availableModules={['users', 'license-hygiene']} />);
    await waitFor(() => {
      expect(screen.getByRole('checkbox', { name: 'License Hygiene' }).hasAttribute('disabled')).toBe(false);
      expect(screen.getByRole('checkbox', { name: 'License Hygiene for member@example.com' }).hasAttribute('disabled')).toBe(false);
    });

    fireEvent.click(screen.getByRole('checkbox', { name: 'License Hygiene for member@example.com' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save modules for member@example.com' }));
    await waitFor(() => expect(api).toHaveBeenCalledWith(
      '/api/workspaces/current/access/memberships/member-1/modules',
      expect.objectContaining({ method: 'PATCH' }),
    ));
    const patch = api.mock.calls.find(([path, init]) => path.endsWith('/modules') && init?.method === 'PATCH')?.[1];
    expect(JSON.parse(patch?.body as string).moduleKeys).toContain('license-hygiene');
  });
});
