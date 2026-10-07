import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import React from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from '../../../src/Web/src/app/App';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import type { AppSession } from '../../../src/Web/src/components/TenantContextHeader';

const probe = vi.hoisted(() => ({ shouldReport: true }));
vi.mock('../../../src/Web/src/features/overview/OverviewPage', async importOriginal => {
  const actual = await importOriginal<typeof import('../../../src/Web/src/features/overview/OverviewPage')>();
  const React = await import('react');
  const { useWorkspaceIssueReporter } = await import('../../../src/Web/src/notifications/WorkspaceNotifications');
  return { ...actual, OverviewPage: function OverviewProbe() {
    const reporter = useWorkspaceIssueReporter();
    React.useEffect(() => { if (probe.shouldReport) reporter.report({ key: 'users:account-a', area: 'users', kind: 'service', severity: 'warning', title: 'Users unavailable', detail: 'Retry.' }); }, [reporter]);
    return React.createElement('p', null, 'Overview probe');
  } };
});

const capabilities: CapabilitySnapshot = { workspaceId: 'workspace-one', evaluatedAt: '2026-09-25T10:00:00Z', sourceState: 'graph_authoritative', capabilities: [] };
const firstSession: AppSession = { user: { displayName: 'Shared Name' }, workspace: { id: 'workspace-one', name: 'Same Workspace', enabledModules: ['users'] }, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } };
const secondSession: AppSession = { ...firstSession, user: { displayName: 'Shared Name' }, workspaceAccess: { role: 'owner', canManageMembers: true, canManageSettings: true } };
const loadCapabilities = async () => capabilities;
const loadHealth = async () => ({ status: 'connected' as const, lastVerifiedAt: null });

afterEach(() => { cleanup(); probe.shouldReport = true; window.history.pushState(null, '', '/'); });

it('drops the prior account issue when a new session has the same workspace and display name', async () => {
  window.history.pushState(null, '', '/overview');
  const { rerender } = render(<App loadCapabilities={loadCapabilities} loadSession={async () => firstSession} loadConnectionHealth={loadHealth} />);
  await screen.findByRole('button', { name: /notifications, 1 need attention/i });
  probe.shouldReport = false;
  rerender(<App loadCapabilities={loadCapabilities} loadSession={async () => secondSession} loadConnectionHealth={loadHealth} />);
  await screen.findByRole('link', { name: 'Workspace Settings' });
  await waitFor(() => expect(screen.getByRole('button', { name: /notifications, 0 need attention/i })).toBeTruthy());
});
