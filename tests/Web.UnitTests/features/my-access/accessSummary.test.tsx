import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccessTransparencyProvider, useAccessTransparency } from '../../../../src/Web/src/features/my-access/accessContext';
import { accessSummaryCapabilityGroups, summarizeAccess } from '../../../../src/Web/src/features/my-access/accessSummary';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import type { Capability, CapabilityDecision, CapabilitySnapshot } from '../../../../src/Web/src/capabilities/capabilityTypes';

const session: AppSession = { user: {}, workspace: { id: 'workspace-1', name: 'Workspace One' } };
afterEach(cleanup);

const mappedCapabilities: Capability[] = [
  'users.view',
  'authentication.methods.view',
  'pim.view',
  'users.create',
  'users.update',
  'users.disable',
  'users.reset_password',
  'users.sessions.revoke',
  'groups.manage_members',
  'authentication.methods.manage',
  'roles.assign',
  'pim.activate',
  'devices.view',
  'devices.bitlocker.metadata',
  'devices.laps.metadata',
  'devices.manage',
  'devices.privileged.manage',
  'devices.bitlocker.reveal',
  'devices.laps.reveal',
  'licenses.view',
  'licenses.assign',
  'audit.view',
  'workspace.settings.manage',
  'workspace.members.manage',
];

function decision(capability: Capability, state = 'allowed', roleState = 'not_applicable', extra: Partial<CapabilityDecision> = {}): CapabilityDecision {
  return {
    capability,
    state: state as CapabilityDecision['state'],
    reasonCode: state === 'allowed' ? 'graph_authoritative' : state,
    roleEvidence: { state: roleState, requiredRoleTemplateIds: [], assignments: [] },
    ...extra,
  };
}

function snapshot(overrides: Partial<CapabilitySnapshot> = {}): CapabilitySnapshot {
  return {
    workspaceId: 'workspace-1',
    evaluatedAt: '2026-10-08T12:00:00Z',
    sourceState: 'graph_authoritative',
    capabilities: mappedCapabilities.map(capability => decision(capability)),
    workspaceModules: [
      { module: 'users', grantSource: 'explicit', enabled: true, effective: true },
      { module: 'devices', grantSource: 'explicit', enabled: true, effective: true },
      { module: 'licenses', grantSource: 'explicit', enabled: true, effective: true },
      { module: 'exchange', grantSource: 'explicit', enabled: true, effective: true },
    ],
    ...overrides,
  };
}

function module(summary: ReturnType<typeof summarizeAccess>, key: string) {
  const found = summary.modules.find(item => item.key === key);
  if (!found) throw new Error(`Missing module summary: ${key}`);
  return found;
}

describe('summarizeAccess', () => {
  it('maps every evaluated API capability exactly once', () => {
    const flattened = accessSummaryCapabilityGroups.flatMap(group => [...group.read, ...group.write]);
    expect(flattened).toHaveLength(24);
    expect([...flattened].sort()).toEqual([...mappedCapabilities].sort());
  });

  it('allows a group only when every decision, role layer, workspace and module gate is evidenced', () => {
    const summary = summarizeAccess(snapshot(), session);
    expect(module(summary, 'users').read.state).toBe('allowed');
    expect(module(summary, 'users').write.state).toBe('allowed');
  });

  it('reports mixed known API outcomes instead of collapsing them to allowed', () => {
    const value = snapshot({
      capabilities: mappedCapabilities.map(capability =>
        decision(capability, capability === 'users.update' ? 'consent_required' : 'allowed')),
    });
    expect(module(summarizeAccess(value, session), 'users').write.state).toBe('mixed');
  });

  it('reports missing decisions as partial evidence', () => {
    const value = snapshot({ capabilities: snapshot().capabilities.filter(item => item.capability !== 'users.update') });
    expect(module(summarizeAccess(value, session), 'users').write.state).toBe('partial');
  });

  it.each([
    ['an absent snapshot', null, session],
    ['a non-authoritative Graph snapshot', snapshot({ sourceState: 'temporarily_unavailable' }), session],
    ['a workspace mismatch', snapshot({ workspaceId: 'workspace-2' }), session],
  ])('marks %s unavailable rather than allowed', (_caseName, value, currentSession) => {
    const summary = summarizeAccess(value, currentSession);
    expect(module(summary, 'users').read.state).toBe('unavailable');
    if (value && value.sourceState !== 'graph_authoritative') {
      expect(module(summary, 'workspace-administration').write.state).toBe('allowed');
    }
  });

  it('distinguishes a disabled module from a missing workspace grant', () => {
    const disabled = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item => item.module === 'users' ? { ...item, enabled: false, effective: false } : item),
    });
    const notGranted = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item => item.module === 'users' ? { ...item, enabled: true, effective: false, grantSource: 'none' } : item),
    });
    expect(module(summarizeAccess(disabled, session), 'users').read.state).toBe('module_disabled');
    expect(module(summarizeAccess(notGranted, session), 'users').read.state).toBe('workspace_not_granted');
  });

  it('treats an enabled owner-inherited module as effective', () => {
    const value = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item => item.module === 'users'
        ? { ...item, grantSource: 'owner_inherited', effective: true }
        : item),
    });
    expect(module(summarizeAccess(value, session), 'users').read.state).toBe('allowed');
  });

  it('keeps Exchange unavailable because Microsoft action coverage is incomplete', () => {
    const summary = summarizeAccess(snapshot(), session);
    expect(module(summary, 'exchange').read.state).toBe('unavailable');
    expect(module(summary, 'exchange').write.state).toBe('unavailable');
  });

  it('keeps platform-only decisions independently usable when Graph is unavailable', () => {
    const value = snapshot({
      sourceState: 'temporarily_unavailable',
      capabilities: mappedCapabilities.map(capability =>
        decision(capability, 'allowed', capability.startsWith('workspace.') || capability === 'audit.view' ? 'not_applicable' : 'unavailable')),
    });
    const summary = summarizeAccess(value, session);
    expect(module(summary, 'workspace-administration').read.state).toBe('allowed');
    expect(module(summary, 'workspace-administration').write.state).toBe('allowed');
    expect(module(summary, 'users').read.state).toBe('unavailable');
  });

  it('does not treat an empty missing-scopes list as proof of complete consent', () => {
    const value = snapshot({
      capabilities: mappedCapabilities.map(capability =>
        decision(capability, capability === 'users.update' ? 'consent_required' : 'allowed', 'not_applicable', capability === 'users.update' ? { missingScopes: [] } : {})),
    });
    const update = module(summarizeAccess(value, session), 'users').write.actions.find(action => action.capability === 'users.update');
    expect(update?.consentEvidence).toBe('The API reported no missing scopes; complete consent is not separately shown.');
  });

  it('downgrades an apparently allowed action when role evidence is unavailable', () => {
    const value = snapshot({
      capabilities: mappedCapabilities.map(capability =>
        decision(capability, 'allowed', capability === 'users.update' ? 'unavailable' : 'not_applicable', capability === 'users.update' ? {
          requiredRoleTemplateId: 'role-1',
        } : {})),
    });
    expect(module(summarizeAccess(value, session), 'users').write.state).toBe('partial');
  });
});

function AccessContextProbe() {
  const { snapshot: currentSnapshot, loading, error, refresh } = useAccessTransparency();
  return <>
    <p role="status">{loading ? 'loading' : error ? 'error' : 'ready'}</p>
    <p>{currentSnapshot?.workspaceId ?? 'no-current-snapshot'}</p>
    <button type="button" onClick={() => void refresh()}>Refresh evidence</button>
  </>;
}

describe('AccessTransparencyProvider', () => {
  it('loads and refreshes the current workspace snapshot through the shared context', async () => {
    const loadSnapshot = vi.fn().mockResolvedValue(snapshot());
    render(<AccessTransparencyProvider session={session} loadSnapshot={loadSnapshot}><AccessContextProbe /></AccessTransparencyProvider>);

    await waitFor(() => expect(screen.getByText('workspace-1')).toBeTruthy());
    expect(screen.getByRole('status').textContent).toBe('ready');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh evidence' }));
    await waitFor(() => expect(loadSnapshot).toHaveBeenCalledTimes(2));
    expect(screen.getByText('workspace-1')).toBeTruthy();
  });

  it('exposes retryable load failure without fabricating a snapshot', async () => {
    const loadSnapshot = vi.fn()
      .mockRejectedValueOnce(new Error('private failure detail'))
      .mockResolvedValue(snapshot());
    render(<AccessTransparencyProvider session={session} loadSnapshot={loadSnapshot}><AccessContextProbe /></AccessTransparencyProvider>);

    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('error'));
    expect(screen.getByText('no-current-snapshot')).toBeTruthy();
    expect(document.body.textContent).not.toContain('private failure detail');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh evidence' }));
    await waitFor(() => expect(screen.getByText('workspace-1')).toBeTruthy());
    expect(screen.getByRole('status').textContent).toBe('ready');
  });
});
