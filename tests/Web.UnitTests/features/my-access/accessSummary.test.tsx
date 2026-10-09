import React from 'react';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccessTransparencyProvider, useAccessTransparency } from '../../../../src/Web/src/features/my-access/accessContext';
import { accessSummaryCapabilityGroups, summarizeAccess } from '../../../../src/Web/src/features/my-access/accessSummary';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import type { Capability, CapabilityDecision, CapabilitySnapshot } from '../../../../src/Web/src/capabilities/capabilityTypes';

vi.mock('../../../../src/Web/src/auth/useApi', () => ({
  useApi: () => async (path: string) => fetch(path),
}));

const session: AppSession = { user: {}, workspace: { id: 'workspace-1', name: 'Workspace One' } };
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

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
  'authentication.campaigns.view',
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
      { module: 'authentication-campaigns', grantSource: 'explicit', enabled: true, effective: true },
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
    expect(flattened).toHaveLength(25);
    expect([...flattened].sort()).toEqual([...mappedCapabilities].sort());
  });

  it('maps authentication campaigns as a read-only module with its exact API decision and workspace gate', () => {
    const summary = summarizeAccess(snapshot(), session);
    const campaigns = module(summary, 'authentication-campaigns');

    expect(campaigns.read.state).toBe('allowed');
    expect(campaigns.read.actions.map(action => action.capability)).toEqual(['authentication.campaigns.view']);
    expect(campaigns.read.actions[0]?.decision?.capability).toBe('authentication.campaigns.view');
    expect(campaigns.write.state).toBe('not_applicable');
    expect(campaigns.write.actions).toEqual([]);

    const notGranted = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item =>
        item.module === 'authentication-campaigns' ? { ...item, effective: false, grantSource: 'none' } : item),
    });
    expect(module(summarizeAccess(notGranted, session), 'authentication-campaigns').read.state).toBe('workspace_not_granted');
    expect(module(summarizeAccess(notGranted, session), 'authentication-campaigns').write.state).toBe('not_applicable');

    const disabled = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item =>
        item.module === 'authentication-campaigns' ? { ...item, enabled: false, effective: false } : item),
    });
    expect(module(summarizeAccess(disabled, session), 'authentication-campaigns').read.state).toBe('module_disabled');
    expect(module(summarizeAccess(disabled, session), 'authentication-campaigns').write.state).toBe('not_applicable');

    const missingModule = snapshot({ workspaceModules: [] });
    expect(module(summarizeAccess(missingModule, session), 'authentication-campaigns').read.state).toBe('unavailable');
    expect(module(summarizeAccess(missingModule, session), 'authentication-campaigns').write.state).toBe('not_applicable');
  });

  it('keeps campaign consent and PIM evidence distinct from missing API decisions', () => {
    const consent = snapshot({
      capabilities: snapshot().capabilities.map(item => item.capability === 'authentication.campaigns.view'
        ? decision(item.capability, 'consent_required', 'available', {
          reasonCode: 'delegated_scope_required',
          missingScopes: ['AuditLog.Read.All'],
          roleEvidence: {
            state: 'available',
            requiredRoleTemplateIds: ['4a5d8f65-41da-4de4-8968-e035b65339cf'],
            assignments: [],
          },
        })
        : item),
    });
    const consentRead = module(summarizeAccess(consent, session), 'authentication-campaigns').read;
    expect(consentRead.state).toBe('consent_required');
    expect(consentRead.actions[0]?.consentEvidence).toContain('AuditLog.Read.All');

    const pim = snapshot({
      capabilities: snapshot().capabilities.map(item => item.capability === 'authentication.campaigns.view'
        ? decision(item.capability, 'pim_activation_required', 'available', {
          reasonCode: 'active_role',
          requiredRoleTemplateId: '4a5d8f65-41da-4de4-8968-e035b65339cf',
          roleEvidence: {
            state: 'available',
            requiredRoleTemplateIds: ['4a5d8f65-41da-4de4-8968-e035b65339cf'],
            assignments: [{
              roleTemplateId: '4a5d8f65-41da-4de4-8968-e035b65339cf',
              assignmentState: 'eligible',
              scope: 'tenant_wide',
              pimState: 'activation_required',
            }],
          },
        })
        : item),
    });
    const pimRead = module(summarizeAccess(pim, session), 'authentication-campaigns').read;
    expect(pimRead.state).toBe('pim_activation_required');
    expect(pimRead.actions[0]?.decision?.roleEvidence?.assignments[0]?.assignmentState).toBe('eligible');

    const missing = snapshot({
      capabilities: snapshot().capabilities.filter(item => item.capability !== 'authentication.campaigns.view'),
    });
    const missingRead = module(summarizeAccess(missing, session), 'authentication-campaigns').read;
    expect(missingRead.state).toBe('partial');
    expect(missingRead.actions[0]?.decision).toBeNull();

    const unknownRoleEvidence = snapshot({
      capabilities: snapshot().capabilities.map(item => item.capability === 'authentication.campaigns.view'
        ? decision(item.capability, 'allowed', 'unavailable')
        : item),
    });
    const unknownRoleRead = module(summarizeAccess(unknownRoleEvidence, session), 'authentication-campaigns').read;
    expect(unknownRoleRead.state).toBe('partial');
    expect(unknownRoleRead.actions[0]?.decision?.state).toBe('allowed');
  });

  it.each([
    'pim_activation_required',
    'pim_approval_required',
    'pim_mfa_required',
    'pim_eligibility_expired',
  ] as const)('preserves campaign %s as a distinct read decision', state => {
    const value = snapshot({
      capabilities: snapshot().capabilities.map(item => item.capability === 'authentication.campaigns.view'
        ? decision(item.capability, state, 'available')
        : item),
    });

    expect(module(summarizeAccess(value, session), 'authentication-campaigns').read.state).toBe(state);
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
      expect(module(summary, 'workspace-administration').write.state).toBe('unavailable');
    }
  });

  it('preserves API decisions and consent evidence when the Graph snapshot is unavailable', () => {
    const value = snapshot({
      sourceState: 'temporarily_unavailable',
      capabilities: mappedCapabilities.map(capability => decision(
        capability,
        capability === 'users.update' ? 'consent_required' : 'allowed',
        'not_applicable',
        capability === 'users.update'
          ? { reasonCode: 'consent_required', missingScopes: ['User.ReadWrite.All'] }
          : {},
      )),
    });

    const update = module(summarizeAccess(value, session), 'users').write.actions.find(action => action.capability === 'users.update');
    expect(update?.state).toBe('unavailable');
    expect(update?.decision?.state).toBe('consent_required');
    expect(update?.reasonLabel).toContain('Microsoft consent is required');
    expect(update?.consentEvidence).toContain('User.ReadWrite.All');
  });

  it.each([
    ['module disabled', { enabled: false, effective: false }, 'module_disabled'],
    ['workspace grant missing', { enabled: true, effective: false }, 'workspace_not_granted'],
  ] as const)('retains role and consent evidence when the module is gated (%s)', (_label, gate, expectedState) => {
    const value = snapshot({
      workspaceModules: snapshot().workspaceModules?.map(item => item.module === 'users' ? { ...item, ...gate } : item),
      capabilities: mappedCapabilities.filter(capability => capability !== 'users.sessions.revoke').map(capability => decision(
        capability,
        capability === 'users.create' ? 'pim_activation_required' : capability === 'users.update' ? 'consent_required' : 'allowed',
        'available',
        capability === 'users.create'
          ? {
            reasonCode: 'directory_role_required',
            requiredRoleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1',
            roleEvidence: {
              state: 'available',
              requiredRoleTemplateIds: ['fe930be7-5e62-47db-91af-98c3a49a38b1'],
              assignments: [{ roleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1', assignmentState: 'eligible', scope: 'tenant' }],
            },
          }
          : capability === 'users.update'
            ? { reasonCode: 'consent_required', missingScopes: ['User.ReadWrite.All'] }
            : {},
      )),
    });

    const users = module(summarizeAccess(value, session), 'users');
    const create = users.write.actions.find(action => action.capability === 'users.create');
    const update = users.write.actions.find(action => action.capability === 'users.update');
    expect(users.write.state).toBe(expectedState);
    expect(users.write.workspaceGateState).toBe(expectedState);
    expect(create?.state).toBe(expectedState);
    expect(create?.decision?.state).toBe('pim_activation_required');
    expect(create?.decision?.roleEvidence?.assignments[0].assignmentState).toBe('eligible');
    expect(update?.decision?.state).toBe('consent_required');
    expect(update?.consentEvidence).toContain('User.ReadWrite.All');
    const missing = users.write.actions.find(action => action.capability === 'users.sessions.revoke');
    expect(missing?.state).toBe('partial');
    expect(missing?.decision).toBeNull();
    expect(missing?.reasonLabel).toContain('did not return a decision');
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
        decision(capability, 'allowed', capability.startsWith('workspace.') ? 'not_applicable' : 'unavailable',
          capability.startsWith('workspace.') ? { reasonCode: 'workspace_platform_role' } : {})),
    });
    const summary = summarizeAccess(value, session);
    expect(module(summary, 'workspace-administration').read.state).toBe('unavailable');
    expect(module(summary, 'workspace-administration').read.actions[0]?.decision?.state).toBe('allowed');
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
  it('forwards the supplied App capability state and callback without making a request', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, json: async () => snapshot() });
    vi.stubGlobal('fetch', fetchSpy);
    const refresh = vi.fn().mockResolvedValue(undefined);
    const value = { snapshot: snapshot(), loading: true, error: false, refresh };
    render(<AccessTransparencyProvider session={session} value={value}><AccessContextProbe /></AccessTransparencyProvider>);

    expect(screen.getByText('workspace-1')).toBeTruthy();
    expect(screen.getByRole('status').textContent).toBe('loading');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh evidence' }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('does not expose a snapshot from a different workspace', () => {
    const value = { snapshot: snapshot({ workspaceId: 'workspace-2' }), loading: false, error: true, refresh: vi.fn() };
    render(<AccessTransparencyProvider session={session} value={value}><AccessContextProbe /></AccessTransparencyProvider>);

    expect(screen.getByText('no-current-snapshot')).toBeTruthy();
    expect(screen.getByRole('status').textContent).toBe('error');
  });
});
