import React from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { WorkspaceAdminForm } from '../../../../src/Web/src/features/workspace-settings/WorkspaceAdminForm';

afterEach(cleanup);
const settings = { displayName: 'Example', enabledModules: [], defaultColumns: [], defaultFilters: {}, supportInstructions: '', defaultTheme: 'light', access: { state: 'allowed' } } as never;

describe('Settings form footer', () => {
  it('tracks unsaved changes, resets and confirms save', async () => {
    const save = vi.fn(async (value: { displayName: string }) => value as never);
    render(<WorkspaceAdminForm settings={settings} saveSettings={save} />);
    const saveButton = screen.getByRole('button', { name: 'Save changes' }) as HTMLButtonElement;
    expect(saveButton.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Workspace display name'), { target: { value: 'Other' } });
    expect(screen.getByText('Unsaved changes')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Reset' }));
    expect((screen.getByLabelText('Workspace display name') as HTMLInputElement).value).toBe('Example');
    fireEvent.change(screen.getByLabelText('Workspace display name'), { target: { value: 'Other' } });
    fireEvent.click(saveButton);
    expect((await screen.findByRole('status')).textContent).toMatch(/^Saved/);
    expect(save).toHaveBeenCalledTimes(1);
  });
});
