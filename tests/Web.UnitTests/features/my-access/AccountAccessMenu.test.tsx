import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { CapabilitySnapshot } from '../../../../src/Web/src/capabilities/capabilityTypes';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import { AccessTransparencyProvider } from '../../../../src/Web/src/features/my-access/accessContext';
import { AccountAccessMenu } from '../../../../src/Web/src/features/my-access/AccountAccessMenu';

const session: AppSession = {
  user: { displayName: 'Alex Morgan' },
  workspace: { id: 'workspace-one', name: 'Workspace One' },
};

const snapshot: CapabilitySnapshot = {
  workspaceId: 'workspace-one',
  evaluatedAt: '2026-10-08T12:00:00Z',
  sourceState: 'graph_authoritative',
  workspaceModules: [{ module: 'users', grantSource: 'direct', enabled: true, effective: true }],
  capabilities: [
    { capability: 'users.view', state: 'allowed', reasonCode: 'active_role', roleEvidence: { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] } },
    { capability: 'authentication.methods.view', state: 'allowed', reasonCode: 'active_role', roleEvidence: { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] } },
    { capability: 'pim.view', state: 'allowed', reasonCode: 'active_role', roleEvidence: { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] } },
  ],
};

function renderMenu({
  currentSnapshot = snapshot,
  loading = false,
  error = false,
  refresh = vi.fn(async () => {}),
  onNavigate = vi.fn(),
}: {
  currentSnapshot?: CapabilitySnapshot | null;
  loading?: boolean;
  error?: boolean;
  refresh?: () => Promise<void>;
  onNavigate?: (path: string) => void;
} = {}) {
  return {
    onNavigate,
    refresh,
    ...render(
      <AccessTransparencyProvider session={session} value={{ snapshot: currentSnapshot, loading, error, refresh }}>
        <AccountAccessMenu session={session} onNavigate={onNavigate} />
      </AccessTransparencyProvider>,
    ),
  };
}

describe('AccountAccessMenu', () => {
  afterEach(cleanup);

  it('shows readable domain read and write summaries without hiding partial evidence', () => {
    const { container } = renderMenu();
    const menus = container.querySelectorAll('details');

    expect(menus).toHaveLength(1);
    const menu = within(menus[0] as HTMLElement);
    expect(menus[0].querySelector('summary')?.textContent).toBe('DOMAIN ACCESS');
    expect(menu.getAllByText(/Read access:/).length).toBeGreaterThan(0);
    expect(menu.getAllByText(/Write access:/).length).toBeGreaterThan(0);
    expect(menu.getAllByText('Write access: Partial evidence').length).toBeGreaterThan(0);
    expect(menu.getByRole('link', { name: 'My access' }).getAttribute('href')).toBe('/my-access');
  });

  it('labels known mixed outcomes as mixed instead of collapsing them to allow or deny', () => {
    const completeRoleEvidence = { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] };
    const mixedSnapshot: CapabilitySnapshot = {
      ...snapshot,
      capabilities: [
        ...snapshot.capabilities,
        { capability: 'workspace.settings.manage', state: 'allowed', reasonCode: 'workspace_platform_role', roleEvidence: completeRoleEvidence },
        { capability: 'workspace.members.manage', state: 'hidden', reasonCode: 'workspace_platform_role_required', roleEvidence: completeRoleEvidence },
      ],
    };
    const { container } = renderMenu({ currentSnapshot: mixedSnapshot });

    expect(container.querySelector('.account-access-menu__modules')?.textContent).toContain('Write access: Mixed results');
    expect(container.querySelector('.account-access-menu__modules')?.textContent).not.toContain('Write access: Allowed');
  });

  it('reports unavailable evidence instead of implying no access and refreshes the shared snapshot', () => {
    const refresh = vi.fn(async () => {});
    renderMenu({ currentSnapshot: null, error: true, refresh });

    expect(screen.getAllByText(/Access evidence unavailable/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/No access/)).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Refresh access' }));
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('distinguishes an initial capability load when no snapshot exists', () => {
    renderMenu({ currentSnapshot: null, loading: true });

    expect(screen.getByRole('status').textContent).toContain('Loading access evidence');
    expect(screen.getByRole('button', { name: 'Refreshing access' }).hasAttribute('disabled')).toBe(true);
    expect(screen.queryByText(/No access/)).toBeNull();
  });

  it('labels retained evidence as stale while the shared refresh is running', () => {
    renderMenu({ loading: true });

    expect(screen.getByText(/Previous access check/)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Refreshing access' }).hasAttribute('disabled')).toBe(true);
  });

  it('labels retained evidence as stale after a failed shared refresh', () => {
    renderMenu({ error: true });

    expect(screen.getByText(/Previous access check/)).toBeTruthy();
    expect(screen.queryByText(/No access/)).toBeNull();
  });

  it.each(['Enter', ' '])('opens the native disclosure with %s', (key) => {
    const { container } = renderMenu();
    const details = container.querySelector('details') as HTMLDetailsElement;
    const summary = details.querySelector('summary')!;

    fireEvent.keyDown(summary, { key });

    expect(details.open).toBe(true);
  });

  it('closes on Escape and restores focus to the disclosure summary', () => {
    const { container } = renderMenu();
    const details = container.querySelector('details') as HTMLDetailsElement;
    const summary = details.querySelector('summary')!;

    fireEvent.click(summary);
    const link = within(details).getByRole('link', { name: 'My access' });
    link.focus();
    fireEvent.keyDown(link, { key: 'Escape' });

    expect(details.open).toBe(false);
    expect(document.activeElement).toBe(summary);
  });

  it('closes before navigating to My access', () => {
    const onNavigate = vi.fn();
    const { container } = renderMenu({ onNavigate });
    const details = container.querySelector('details') as HTMLDetailsElement;
    fireEvent.click(details.querySelector('summary')!);
    fireEvent.click(within(details).getByRole('link', { name: 'My access' }));

    expect(details.open).toBe(false);
    expect(onNavigate).toHaveBeenCalledWith('/my-access');
  });
});
