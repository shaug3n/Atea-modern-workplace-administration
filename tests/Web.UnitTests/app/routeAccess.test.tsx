import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../../src/Web/src/app/App';
import { getAuthorizedAppRoutes, getRouteAccess } from '../../../src/Web/src/app/App';
import { appRoutes } from '../../../src/Web/src/app/routes';
import type { CapabilityDecision, CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';

const apiMock = vi.hoisted(() => vi.fn());
vi.mock('../../../src/Web/src/auth/useApi', () => ({ useApi: () => apiMock }));

const workspaceId = '55555555-5555-5555-5555-555555555555';
const session = {
  user: { displayName: 'Alex Morgan' },
  workspace: {
    id: workspaceId,
    name: 'Contoso Workplace',
    enabledModules: ['about', 'users'],
    moduleAccess: ['about', 'users'],
  },
};

const aboutAllowed: CapabilityDecision = {
  capability: 'platform.about.view',
  state: 'allowed',
  reasonCode: 'workspace_member',
};

describe('route access metadata', () => {
  afterEach(() => {
    cleanup();
    apiMock.mockReset();
    window.history.pushState(null, '', '/');
  });

  it('uses the rendering gates to return only authorized application routes', () => {
    const decisions: CapabilityDecision[] = [
      aboutAllowed,
      { capability: 'users.view', state: 'hidden', reasonCode: 'role_required' },
    ];

    expect(getRouteAccess(appRoutes.find(route => route.path === '/about')!, session, decisions))
      .toEqual({ workspaceAllowed: true, moduleAllowed: true, decision: aboutAllowed });

    const routes = getAuthorizedAppRoutes(session, decisions);
    expect(routes).toContainEqual({ path: '/about', label: 'About' });
    expect(routes).toContainEqual({ path: '/about/system-versions', label: 'System versions' });
    expect(routes.map(route => route.path)).not.toContain('/users');
    expect(routes.map(route => route.path)).not.toContain('/settings');
    expect(routes.some(route => route.path.startsWith('/api/'))).toBe(false);
  });

  it('excludes about routes when the module is disabled, unassigned, or the capability is not allowed', () => {
    const routesFor = (workspace: typeof session['workspace'], decisions: CapabilityDecision[]) =>
      getAuthorizedAppRoutes({ ...session, workspace }, decisions).map(route => route.path);

    expect(routesFor({ ...session.workspace, enabledModules: ['users'] }, [aboutAllowed]))
      .not.toContain('/about');
    expect(routesFor({ ...session.workspace, moduleAccess: ['users'] }, [aboutAllowed]))
      .not.toContain('/about/system-versions');
    expect(routesFor(session.workspace, [{ ...aboutAllowed, state: 'hidden' }]))
      .not.toContain('/about');
    expect(routesFor(session.workspace, []))
      .not.toContain('/about');
  });

  it('keeps parameterized application routes as patterns and excludes explicit callback routes', () => {
    const routes = getAuthorizedAppRoutes(session, [
      aboutAllowed,
      { capability: 'users.view', state: 'read_only', reasonCode: 'role_read_only' },
    ]);
    const paths = routes.map(route => route.path);

    expect(paths).toContain('/users/:userId');
    expect(paths).not.toContain('/consent-callback');
    expect(paths).not.toContain('/onboarding/consent/callback');
    expect(paths.some(path => path.startsWith('/api/'))).toBe(false);
    expect(appRoutes.filter(route => route.path.includes('callback'))
      .every(route => route.includeInSystemInventory === false)).toBe(true);
  });

  it('allows a verified platform About decision during Graph unavailability without relaxing Graph-backed routes', async () => {
    window.history.pushState(null, '', '/about');
    const platformSnapshot: CapabilitySnapshot = {
      workspaceId,
      evaluatedAt: '2026-10-09T08:00:00Z',
      sourceState: 'temporarily_unavailable',
      capabilities: [aboutAllowed],
    };

    render(<App loadCapabilities={async () => platformSnapshot} loadSession={async () => session} />);
    expect(await screen.findByRole('tab', { name: 'Overview' })).toBeTruthy();
    cleanup();

    window.history.pushState(null, '', '/users');
    const graphSnapshot: CapabilitySnapshot = {
      ...platformSnapshot,
      capabilities: [{ capability: 'users.view', state: 'allowed', reasonCode: 'active_role' }],
    };
    render(<App loadCapabilities={async () => graphSnapshot} loadSession={async () => session} />);

    expect(await screen.findByText(/Data cannot be shown right now/)).toBeTruthy();
  });

  it('does not allow the platform route when workspace identity or returned decision is missing', async () => {
    window.history.pushState(null, '', '/about');
    render(<App
      loadCapabilities={async () => ({
        workspaceId: 'different-workspace',
        evaluatedAt: '2026-10-09T08:00:00Z',
        sourceState: 'temporarily_unavailable',
        capabilities: [aboutAllowed],
      })}
      loadSession={async () => session}
    />);
    expect(await screen.findByText(/Data cannot be shown right now/)).toBeTruthy();
  });

  it('uses the same authorized dialog opener from the Feedback page, shell launcher and shortcut', async () => {
    window.history.pushState(null, '', '/feedback');
    apiMock
      .mockResolvedValueOnce(Response.json({ items: [], nextCursor: null }))
      .mockResolvedValueOnce(Response.json({ id: '22222222-2222-4222-8222-222222222222', createdAt: '2026-10-09T08:00:00Z', expiresAt: '2027-01-07T08:00:00Z' }, { status: 201 }))
      .mockResolvedValueOnce(Response.json({ items: [], nextCursor: null }));
    const feedbackWorkspace = {
      ...session,
      user: { ...session.user, objectId: 'user-1' },
      workspace: { ...session.workspace, enabledModules: ['feedback'], moduleAccess: ['feedback'] },
    };
    const feedbackSnapshot: CapabilitySnapshot = {
      workspaceId,
      evaluatedAt: '2026-10-09T08:00:00Z',
      sourceState: 'graph_authoritative',
      capabilities: [{ capability: 'feedback.submit', state: 'allowed', reasonCode: 'workspace_member' }],
    };
    render(<App loadCapabilities={async () => feedbackSnapshot} loadSession={async () => feedbackWorkspace} />);

    expect(await screen.findByRole('heading', { name: 'No feedback yet' })).toBeTruthy();
    fireEvent.keyDown(document, { key: 'F', altKey: true, shiftKey: true });
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'General' } });
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'App flow' } });
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Saved through the shared dialog' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save feedback' }));
    expect((await screen.findByRole('status')).textContent).toBe('Saved');
    await waitFor(() => expect(apiMock).toHaveBeenCalledTimes(3));
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Give feedback' })[0]);
    expect(screen.getAllByRole('dialog')).toHaveLength(1);
  });

  it('shows independent web and API builds and only authorized front-end routes', async () => {
    window.history.pushState(null, '', '/about/system-versions');
    apiMock.mockResolvedValue(Response.json({
      productVersion: '0.2.0',
      commit: 'api-commit',
      branch: 'release',
    }));
    const snapshot: CapabilitySnapshot = {
      workspaceId,
      evaluatedAt: '2026-10-09T08:00:00Z',
      sourceState: 'graph_authoritative',
      capabilities: [
        aboutAllowed,
        { capability: 'users.view', state: 'hidden', reasonCode: 'role_required' },
      ],
    };

    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);

    expect(await screen.findByRole('heading', { name: 'System versions' })).toBeTruthy();
    expect(await screen.findByText('api-commit')).toBeTruthy();
    expect(screen.getByText('API')).toBeTruthy();
    expect(screen.getByText('Web application')).toBeTruthy();
    const inventory = (await screen.findByRole('heading', { name: 'Authorized application routes' })).parentElement!;
    expect(inventory.textContent).toContain('/about/system-versions');
    expect(inventory.textContent).not.toContain('/users');
    expect(inventory.textContent).not.toContain('/api/');
    expect(apiMock).toHaveBeenCalledWith('/api/about/system-versions', { cache: 'no-store' });
  });

  it('does not disclose cached Graph routes in the inventory when the Graph snapshot is unavailable', async () => {
    window.history.pushState(null, '', '/about/system-versions');
    apiMock.mockResolvedValue(Response.json({ productVersion: '0.2.0' }));
    const snapshot: CapabilitySnapshot = {
      workspaceId,
      evaluatedAt: '2026-10-09T08:00:00Z',
      sourceState: 'temporarily_unavailable',
      capabilities: [
        aboutAllowed,
        { capability: 'users.view', state: 'allowed', reasonCode: 'active_role' },
      ],
    };

    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);

    const inventory = (await screen.findByRole('heading', { name: 'Authorized application routes' })).parentElement!;
    expect(inventory.textContent).toContain('/about/system-versions');
    expect(inventory.textContent).not.toContain('/users');
  });

  it('preserves API HTTP failures and labels missing API build fields unavailable', async () => {
    window.history.pushState(null, '', '/about/system-versions');
    apiMock.mockResolvedValue(Response.json({}, { status: 503 }));
    const snapshot: CapabilitySnapshot = {
      workspaceId,
      evaluatedAt: '2026-10-09T08:00:00Z',
      sourceState: 'graph_authoritative',
      capabilities: [aboutAllowed],
    };

    render(<App loadCapabilities={async () => snapshot} loadSession={async () => session} />);

    expect((await screen.findByRole('alert')).textContent).toContain('HTTP 503');
    expect(screen.getAllByText('Unavailable').length).toBeGreaterThanOrEqual(3);
  });
});
