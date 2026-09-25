import { cleanup, render, screen, waitFor } from '@testing-library/react';
import React, { useEffect } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type { CapabilitySnapshot } from '../../../src/Web/src/capabilities/capabilityTypes';
import type { AppSession } from '../../../src/Web/src/components/TenantContextHeader';
import { deriveCapabilityIssues } from '../../../src/Web/src/notifications/workspaceIssues';
import { WorkspaceNotificationsProvider, useWorkspaceIssueReporter, useWorkspaceNotifications } from '../../../src/Web/src/notifications/WorkspaceNotifications';

const session: AppSession = { user: { displayName: 'Alex' }, workspace: { id: 'one', name: 'One', enabledModules: ['users', 'devices'] }, workspaceAccess: { role: 'member', canManageMembers: false, canManageSettings: false } };
const snapshot = (capabilities: CapabilitySnapshot['capabilities']): CapabilitySnapshot => ({ workspaceId: 'one', evaluatedAt: '2026-09-25T10:00:00Z', sourceState: 'graph_authoritative', capabilities });

afterEach(cleanup);

describe('workspace issues', () => {
  it('collapses matching capability causes by affected area', () => {
    const issues = deriveCapabilityIssues(snapshot([
      { capability: 'users.create', state: 'consent_required', reasonCode: 'consent_required' },
      { capability: 'users.update', state: 'consent_required', reasonCode: 'consent_required' },
      { capability: 'devices.manage', state: 'consent_required', reasonCode: 'consent_required' },
    ]), session);
    expect(issues).toHaveLength(2);
    expect(issues.map(issue => issue.area).sort()).toEqual(['devices', 'users']);
  });

  it('keeps read-only access informational', () => {
    const issues = deriveCapabilityIssues(snapshot([{ capability: 'users.update', state: 'read_only', reasonCode: 'role_read_only' }]), session);
    expect(issues).toHaveLength(1);
    expect(issues[0]).toMatchObject({ area: 'Your access', kind: 'access', severity: 'info' });
  });

  it('retains a safe PIM activation action', () => {
    const issues = deriveCapabilityIssues(snapshot([{ capability: 'users.reset_password', state: 'pim_activation_required', reasonCode: 'pim_activation_required', nextStep: { label: 'Open PIM guidance', href: '/identity' } }]), session);
    expect(issues[0].action).toEqual({ label: 'Open PIM guidance', href: '/identity' });
  });

  it('describes unknown scope evidence as unavailable verification', () => {
    const issues = deriveCapabilityIssues(snapshot([{ capability: 'devices.view', state: 'temporarily_unavailable', reasonCode: 'scope_probe_unavailable', missingScopes: ['Secret.Scope'] }]), session);
    expect(issues[0].detail).toMatch(/verification is unavailable/i);
    expect(JSON.stringify(issues)).not.toContain('Secret.Scope');
  });

  it('guides non-admins to contact an administrator without a settings link', () => {
    const issues = deriveCapabilityIssues(snapshot([{ capability: 'users.update', state: 'consent_required', reasonCode: 'consent_required', nextStep: { label: 'Open Setup', href: '/settings/setup' } }]), session);
    expect(issues[0].detail).toMatch(/contact.*administrator/i);
    expect(issues[0].action?.href).toBeUndefined();
  });

  it('drops reported issues when the workspace changes', async () => {
    function Probe({ report }: { report: boolean }) {
      const reporter = useWorkspaceIssueReporter();
      const { issues } = useWorkspaceNotifications();
      useEffect(() => { if (report) reporter.report({ key: 'users:failure', area: 'users', kind: 'service', severity: 'warning', title: 'Users unavailable', detail: 'Try again.' }); }, [report, reporter]);
      return <span>{issues.some(issue => issue.area === 'users') ? 'reported' : 'cleared'}</span>;
    }
    const health = async () => ({ status: 'connected' as const, lastVerifiedAt: null });
    const { rerender } = render(<WorkspaceNotificationsProvider session={session} capabilities={null} capabilitiesError={null} onRefresh={async () => {}} loadConnectionHealth={health}><Probe report /></WorkspaceNotificationsProvider>);
    await screen.findByText('reported');
    rerender(<WorkspaceNotificationsProvider session={{ ...session, workspace: { ...session.workspace, id: 'two' } }} capabilities={null} capabilitiesError={null} onRefresh={async () => {}} loadConnectionHealth={health}><Probe report={false} /></WorkspaceNotificationsProvider>);
    await waitFor(() => expect(screen.getByText('cleared')).toBeTruthy());
  });

  it('does not retain reported API response text or unsafe actions', async () => {
    function Probe() {
      const reporter = useWorkspaceIssueReporter();
      const { issues } = useWorkspaceNotifications();
      useEffect(() => { reporter.report({ key: 'devices:failure', area: 'devices', kind: 'service', severity: 'warning', title: 'token secret-token', detail: 'Graph error secret-token', action: { label: 'Open token', href: 'https://evil.example/token' }, correlationId: 'secret token' }); }, [reporter]);
      return <pre>{JSON.stringify(issues)}</pre>;
    }
    render(<WorkspaceNotificationsProvider session={session} capabilities={null} capabilitiesError={null} onRefresh={async () => {}}><Probe /></WorkspaceNotificationsProvider>);
    await waitFor(() => expect(screen.getByText(/Data temporarily unavailable/)).toBeTruthy());
    expect(document.body.textContent).not.toContain('secret-token');
    expect(document.body.textContent).not.toContain('evil.example');
    expect(document.body.textContent).not.toContain('secret token');
  });
});
