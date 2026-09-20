import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import { App } from '../../../src/Web/src/app/App';
import { AppShell } from '../../../src/Web/src/components/AppShell';
import { ThemeProvider } from '../../../src/Web/src/components/ThemeToggle';

const allowedCapabilities: CapabilitySnapshot = {
  workspaceId: '55555555-5555-5555-5555-555555555555',
  evaluatedAt: '2026-09-20T10:00:00.000Z',
  sourceState: 'graph_authoritative',
  capabilities: [
    { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'licenses.assign', state: 'allowed', reasonCode: 'active_role' },
    { capability: 'workspace.settings.manage', state: 'allowed', reasonCode: 'workspace_platform_role' },
  ],
};

const hiddenWorkspaceSettings: CapabilitySnapshot = {
  ...allowedCapabilities,
  capabilities: allowedCapabilities.capabilities.map((decision) =>
    decision.capability === 'workspace.settings.manage'
      ? { ...decision, state: 'hidden' as const, reasonCode: 'workspace_platform_role_required' }
      : decision),
};

const session = {
  user: { displayName: 'Alex Morgan', userPrincipalName: 'alex@example.com' },
  workspace: { id: '55555555-5555-5555-5555-555555555555', name: 'Contoso Workplace' },
};

describe('AppShell', () => {
  afterEach(() => {
    cleanup();
    window.history.pushState(null, '', '/');
    document.documentElement.removeAttribute('data-theme');
  });

  it('renders the Atea logo once as the home link in the application shell', async () => {
    render(
      <ThemeProvider systemTheme={() => 'light'}>
        <AppShell capabilities={allowedCapabilities} currentPath="/overview" session={session}>
          <p>Overview content</p>
        </AppShell>
      </ThemeProvider>
    );

    const logoLink = screen.getByRole('link', { name: 'Atea Unified Workplace home' });
    expect(logoLink.getAttribute('href')).toBe('/overview');
    expect(logoLink.querySelectorAll('img[data-logo-asset="atea-logo-grey.svg"]')).toHaveLength(1);
    expect(screen.getByText('Contoso Workplace')).toBeTruthy();
    expect(screen.getByText('Alex Morgan')).toBeTruthy();
  });

  it('keeps direct Workspace settings routes reachable as a permission state when navigation is hidden', async () => {
    window.history.pushState(null, '', '/workspace-settings');
    render(<App loadCapabilities={async () => hiddenWorkspaceSettings} loadSession={async () => session} />);

    await waitFor(() => expect(screen.queryByRole('link', { name: 'Workspace settings' })).toBeNull());
    expect(screen.getByRole('status').textContent).toContain('not available');
  });
});
