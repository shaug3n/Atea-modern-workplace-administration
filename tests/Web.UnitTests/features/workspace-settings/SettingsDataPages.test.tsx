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
});
