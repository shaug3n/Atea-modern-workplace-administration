import React from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { WorkspaceSettingsPage } from '../../../../src/Web/src/features/workspace-settings/WorkspaceSettingsPage';

describe('WorkspaceSettingsPage', () => {
  afterEach(cleanup);

  it('renders and saves the allowlisted workspace settings', async () => {
    const save = async () => ({ displayName: 'Operations', enabledModules: ['overview'], defaultColumns: ['displayName'], defaultFilters: {}, supportInstructions: 'Contact support.', defaultTheme: 'light', access: { state: 'allowed' } });
    render(<WorkspaceSettingsPage loadSettings={async () => ({ displayName: 'Example', enabledModules: ['overview'], defaultColumns: ['displayName'], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } })} saveSettings={save} />);
    await waitFor(() => expect((screen.getByLabelText('Workspace display name') as HTMLInputElement).value).toBe('Example'));
    expect(screen.getByRole('button', { name: 'Save settings' })).toBeTruthy();
  });

  it('shows no-permission state for a hidden platform capability', async () => {
    render(<WorkspaceSettingsPage loadSettings={async () => ({ displayName: '', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'hidden' } })} />);
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Permission required' })).toBeTruthy());
    expect(screen.queryByRole('button', { name: 'Save settings' })).toBeNull();
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
    await waitFor(() => expect(screen.getByText(/Source: Workspace settings.*Retrieved:/)).toBeTruthy());
  });
});
