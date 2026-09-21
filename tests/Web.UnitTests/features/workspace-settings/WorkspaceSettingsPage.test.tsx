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
});
