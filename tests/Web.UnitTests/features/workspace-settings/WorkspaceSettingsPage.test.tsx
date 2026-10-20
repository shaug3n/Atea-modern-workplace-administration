import React from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceSettingsPage } from '../../../../src/Web/src/features/workspace-settings/WorkspaceSettingsPage';

const api = vi.hoisted(() => vi.fn());
vi.mock('../../../../src/Web/src/auth/useApi', () => ({ useApi: () => api }));

describe('WorkspaceSettingsPage', () => {
  afterEach(() => { cleanup(); api.mockReset(); });

  it('submits only permitted General fields and confirms the saved response', async () => {
    const settings = { displayName: 'Example', enabledModules: ['users'], defaultColumns: ['displayName'], defaultFilters: { userType: 'Member' }, supportInstructions: 'Contact support.', defaultTheme: 'light', access: { state: 'allowed' } };
    let submitted: unknown;
    api.mockImplementation(async (path: string, init?: RequestInit) => {
      if (path === '/api/workspaces/current/settings' && init?.method === 'PATCH') {
        submitted = JSON.parse(String(init.body));
        return Response.json({ ...settings, displayName: 'Operations' });
      }
      return Response.json(settings);
    });
    render(<WorkspaceSettingsPage />);
    fireEvent.change(await screen.findByLabelText('Workspace display name'), { target: { value: 'Operations' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
    expect(await screen.findByText(/^Saved/)).toBeTruthy();
    expect(submitted).toEqual({ displayName: 'Operations', defaultColumns: ['displayName'], defaultFilters: { userType: 'Member' }, supportInstructions: 'Contact support.', defaultTheme: 'light' });
    expect(submitted).not.toHaveProperty('enabledModules');
    expect(submitted).not.toHaveProperty('access');
  });

  it('renders and saves the allowlisted workspace settings', async () => {
    const save = async () => ({ displayName: 'Operations', enabledModules: ['overview'], defaultColumns: ['displayName'], defaultFilters: {}, supportInstructions: 'Contact support.', defaultTheme: 'light', access: { state: 'allowed' } });
    render(<WorkspaceSettingsPage loadSettings={async () => ({ displayName: 'Example', enabledModules: ['overview'], defaultColumns: ['displayName'], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } })} saveSettings={save} />);
    await waitFor(() => expect((screen.getByLabelText('Workspace display name') as HTMLInputElement).value).toBe('Example'));
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeTruthy();
  });

  it('shows no-permission state for a hidden platform capability', async () => {
    render(<WorkspaceSettingsPage loadSettings={async () => ({ displayName: '', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'hidden' } })} />);
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Permission required' })).toBeTruthy());
    expect(screen.queryByRole('button', { name: 'Save changes' })).toBeNull();
  });

  it('keeps the General page header through loading, error and retry', async () => {
    let attempts = 0;
    render(<WorkspaceSettingsPage loadSettings={async () => {
      if (++attempts === 1) throw new Error('private settings detail');
      return { displayName: 'Example', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } };
    }} />);
    expect(screen.getByRole('heading', { name: 'General' })).toBeTruthy();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toBeTruthy());
    expect(screen.getByRole('heading', { name: 'General' })).toBeTruthy();
    expect(document.body.textContent).not.toContain('private settings detail');
    screen.getByRole('button', { name: 'Retry' }).click();
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
  });

  it('uses_the_shared_loading_and_permission_states', async () => {
    let finish!: (value: { displayName: string; enabledModules: string[]; defaultColumns: string[]; defaultFilters: Record<string, string>; supportInstructions: string; defaultTheme: 'light'; access: { state: string } }) => void;
    const pending = new Promise<Parameters<typeof finish>[0]>(resolve => { finish = resolve; });
    render(<WorkspaceSettingsPage loadSettings={() => pending} />);
    expect(screen.getByRole('status').textContent).toContain('Loading workspace settings…');
    expect(document.querySelector('.loading-skeleton')).toBeTruthy();

    await act(async () => { finish({ displayName: '', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'hidden' } }); });
    expect(await screen.findByRole('heading', { name: 'Permission required' })).toBeTruthy();
    expect(document.querySelector('.workspace-data-state--permission')).toBeTruthy();
  });
});
