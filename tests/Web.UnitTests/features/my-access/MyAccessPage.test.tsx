import React from 'react';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AccessTransparencyProvider } from '../../../../src/Web/src/features/my-access/accessContext';
import { MyAccessPage } from '../../../../src/Web/src/features/my-access/MyAccessPage';
import type { AppSession } from '../../../../src/Web/src/components/TenantContextHeader';
import type { Capability, CapabilityDecision, CapabilitySnapshot } from '../../../../src/Web/src/capabilities/capabilityTypes';

const session: AppSession = { user: { displayName: 'Avery Member' }, workspace: { id: 'workspace-1', name: 'Workspace One' } };
const capabilities: Capability[] = [
  'users.view', 'authentication.methods.view', 'pim.view', 'users.create', 'users.update', 'users.disable',
  'users.reset_password', 'users.sessions.revoke', 'groups.manage_members', 'authentication.methods.manage',
  'authentication.campaigns.view', 'roles.assign', 'pim.activate', 'devices.view', 'devices.bitlocker.metadata', 'devices.laps.metadata',
  'devices.manage', 'devices.privileged.manage', 'devices.bitlocker.reveal', 'devices.laps.reveal',
  'licenses.view', 'licenses.assign', 'audit.view', 'workspace.settings.manage', 'workspace.members.manage',
];

function decision(capability: Capability, overrides: Partial<CapabilityDecision> = {}): CapabilityDecision {
  return {
    capability,
    state: 'allowed',
    reasonCode: 'graph_authoritative',
    roleEvidence: { state: 'not_applicable', requiredRoleTemplateIds: [], assignments: [] },
    ...overrides,
  };
}

function snapshot(overrides: Partial<CapabilitySnapshot> = {}): CapabilitySnapshot {
  return {
    workspaceId: 'workspace-1',
    evaluatedAt: '2026-10-08T12:00:00Z',
    sourceState: 'graph_authoritative',
    capabilities: capabilities.map(capability => decision(capability)),
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

function renderPage({
  currentSession = session,
  currentSnapshot = snapshot(),
  loading = false,
  error = false,
  refresh = vi.fn().mockResolvedValue(undefined),
  onNavigate,
}: {
  currentSession?: AppSession;
  currentSnapshot?: CapabilitySnapshot | null;
  loading?: boolean;
  error?: boolean;
  refresh?: () => Promise<void>;
  onNavigate?: (path: string) => void;
} = {}) {
  return render(
    <AccessTransparencyProvider
      session={currentSession}
      value={{ snapshot: currentSnapshot, loading, error, refresh }}
    >
      <MyAccessPage session={currentSession} onNavigate={onNavigate} />
    </AccessTransparencyProvider>,
  );
}

afterEach(() => cleanup());

describe('MyAccessPage', () => {
  it('renders_initial_loading_with_busy_semantics', () => {
    renderPage({ currentSnapshot: null, loading: true });

    expect(screen.getByRole('region', { name: 'My access evidence' }).getAttribute('aria-busy')).toBe('true');
    expect(screen.getByText(/Loading access information/)).toBeTruthy();
  });

  it('shows authentication campaign read evidence and does not invent write actions', () => {
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => capability === 'authentication.campaigns.view'
        ? decision(capability, {
          reasonCode: 'active_role',
          requiredRoleTemplateId: '4a5d8f65-41da-4de4-8968-e035b65339cf',
          roleEvidence: {
            state: 'available',
            requiredRoleTemplateIds: ['4a5d8f65-41da-4de4-8968-e035b65339cf'],
            assignments: [{
              roleTemplateId: '4a5d8f65-41da-4de4-8968-e035b65339cf',
              assignmentState: 'active',
              scope: 'tenant_wide',
            }],
          },
        })
        : decision(capability)),
    });
    renderPage({ currentSnapshot });

    const campaigns = screen.getByRole('article', { name: 'Authentication campaigns' });
    const [read, write] = within(campaigns).getAllByRole('group');
    expect(within(read).getByRole('heading', { name: 'View authentication campaigns' })).toBeTruthy();
    expect(within(read).getByText('Reports Reader')).toBeTruthy();
    expect(within(read).getAllByText('Allowed')).toHaveLength(2);
    expect(within(write).getByText('Not applicable')).toBeTruthy();
    expect(within(write).getByText('The API does not currently evaluate actions in this group.')).toBeTruthy();
    expect(within(write).queryByText(/Manage authentication campaigns/i)).toBeNull();
  });

  it('renders_unavailable_and_retry_after_initial_failure', () => {
    const refresh = vi.fn().mockResolvedValue(undefined);
    renderPage({ currentSnapshot: null, error: true, refresh });

    fireEvent.click(screen.getByRole('button', { name: /retry/i }));
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/Access information could not be loaded/)).toBeTruthy();
  });

  it('does_not_render_snapshot_for_another_workspace', () => {
    const oldSnapshot = snapshot({
      workspaceId: 'workspace-old',
      evaluatedAt: '2020-01-01T00:00:00Z',
      workspaceModules: [{ module: 'users', grantSource: 'explicit', enabled: true, effective: true }],
      capabilities: [decision('users.view', {
        state: 'allowed',
        requiredRoleTemplateId: 'role-from-old-workspace',
        nextStep: { label: 'Old decision', href: 'https://invalid.example' },
      })],
    });
    renderPage({ currentSnapshot: oldSnapshot });

    expect(screen.getByText(/another workspace cannot be shown here/)).toBeTruthy();
    expect(screen.queryByText(/role-from-old-workspace|Old decision|January 1, 2020/i)).toBeNull();
    expect(screen.queryByText(/Access to users/)).toBeNull();
    expect(screen.getByRole('button', { name: /retry/i })).toBeTruthy();
  });

  it('keeps_workspace_grants_visible_when_graph_evidence_is_unavailable', () => {
    renderPage({ currentSnapshot: snapshot({ sourceState: 'temporarily_unavailable' }) });

    expect(screen.getAllByText(/Workspace grant: granted/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Microsoft authorization evidence is unavailable/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/Workspace grant: denied/)).toBeNull();
  });

  it('shows unverified consent evidence and missing scopes from an unavailable snapshot', () => {
    const currentSnapshot = snapshot({
      sourceState: 'temporarily_unavailable',
      capabilities: capabilities.map(capability => decision(capability, capability === 'users.update'
        ? { state: 'consent_required', reasonCode: 'consent_required', missingScopes: ['User.ReadWrite.All'] }
        : {})),
    });
    renderPage({ currentSnapshot });

    const updateAction = screen.getByRole('heading', { name: /Edit users/ }).closest('li')!;
    expect(within(updateAction).getByText('Unavailable')).toBeTruthy();
    expect(within(updateAction).getByText(/API returned consent_required.*not verified/i)).toBeTruthy();
    expect(within(updateAction).getAllByText(/User.ReadWrite.All/).length).toBeGreaterThan(0);
    expect(within(updateAction).getAllByText(/Microsoft authorization evidence is unavailable/).length).toBeGreaterThan(0);
    expect(within(updateAction).queryByRole('link', { name: /Microsoft permission setup/i })).toBeNull();
    expect(updateAction.textContent ?? '').toContain('workspace administrator');
  });

  it.each([
    ['module disabled', { enabled: false, effective: false }, 'Module disabled'],
    ['workspace access not granted', { enabled: true, effective: false }, 'Workspace access not granted'],
  ] as const)('retains API role assignment and consent evidence when the module is %s', (_label, gate, status) => {
    const currentSnapshot = snapshot({
      workspaceModules: [{ module: 'users', grantSource: gate.enabled ? 'none' : 'explicit', ...gate }],
      capabilities: capabilities.map(capability => decision(capability, capability === 'users.create'
        ? {
          state: 'pim_activation_required',
          reasonCode: 'directory_role_required',
          requiredRoleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1',
          roleEvidence: {
            state: 'available',
            requiredRoleTemplateIds: ['fe930be7-5e62-47db-91af-98c3a49a38b1'],
            assignments: [{ roleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1', assignmentState: 'eligible', scope: 'tenant' }],
          },
        }
        : capability === 'users.update'
          ? { state: 'consent_required', reasonCode: 'consent_required', missingScopes: ['User.ReadWrite.All'] }
          : {})),
    });
    renderPage({ currentSnapshot });

    const createAction = screen.getByRole('heading', { name: /Create users/ }).closest('li')!;
    const updateAction = screen.getByRole('heading', { name: /Edit users/ }).closest('li')!;
    expect(within(createAction).getByText(status)).toBeTruthy();
    expect(within(createAction).getByText(/Eligible assignment/)).toBeTruthy();
    expect(within(createAction).getByText(/qualifying Microsoft role is required/i)).toBeTruthy();
    expect(within(updateAction).getByText(status)).toBeTruthy();
    expect(within(updateAction).getAllByText(/User.ReadWrite.All/).length).toBeGreaterThan(0);
  });

  it('renders all recognized qualifying role alternatives without calling one required', () => {
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => decision(capability, capability === 'users.update'
        ? {
          state: 'pim_activation_required',
          reasonCode: 'directory_role_required',
          roleEvidence: {
            state: 'available',
            requiredRoleTemplateIds: [
              'f2ef992c-3afb-46b9-b7cf-a126ee74c451',
              'fdd7a751-b60b-444a-984c-02652fe8fa1c',
              'c4e39bd9-1100-46d3-8c65-fb160da0071f',
              '4d6ac14f-3453-41d0-bef9-a3e0c569773a',
            ],
            assignments: [],
          },
        }
        : {})),
    });
    renderPage({ currentSnapshot });

    const updateAction = screen.getByRole('heading', { name: /Edit users/ }).closest('li')!;
    expect(within(updateAction).getByText(/Qualifying role alternatives/)).toBeTruthy();
    for (const name of ['Global Reader', 'Groups Administrator', 'Authentication Administrator', 'License Administrator']) {
      expect(within(updateAction).getByText(name)).toBeTruthy();
    }
    expect(within(updateAction).queryByText(/^Required role:/)).toBeNull();
  });

  it('routes API consent markers to workspace connection settings only for a settings manager', () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => decision(capability,
        capability === 'users.create'
          ? { state: 'consent_required', reasonCode: 'consent_required', nextStep: { label: 'Review setup', href: '/api/workspaces/current/consent/start' } }
          : capability === 'users.update'
            ? { state: 'consent_required', reasonCode: 'consent_required', nextStep: { label: 'Unsupported setup', href: '/api/workspaces/current/consent/start?unsupported=true' } }
            : {})),
    });
    const managerSession = {
      ...session,
      workspaceAccess: { role: 'workspace_owner', canManageSettings: true },
    } satisfies AppSession;
    const onNavigate = vi.fn();
    const { unmount } = renderPage({ currentSession: managerSession, currentSnapshot, onNavigate });
    const setupLink = screen.getByRole('link', { name: 'Review setup' });
    expect(setupLink.getAttribute('href')).toBe('/settings#connection');
    expect(screen.queryByRole('link', { name: 'Unsupported setup' })).toBeNull();
    fireEvent.click(setupLink);
    expect(onNavigate).toHaveBeenCalledWith('/settings#connection');
    expect(fetchSpy).not.toHaveBeenCalled();
    unmount();

    renderPage({ currentSnapshot });
    expect(screen.queryByRole('link', { name: 'Review setup' })).toBeNull();
    expect(screen.getAllByText(/contact your workspace administrator/).length).toBeGreaterThan(0);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('marks_graph_authoritative_workspace_administration_actions_unavailable_without_hiding_platform_decisions', () => {
    const currentSnapshot = snapshot({
      sourceState: 'temporarily_unavailable',
      capabilities: capabilities.map(capability => decision(capability,
        capability === 'audit.view'
          ? { state: 'allowed', reasonCode: 'graph_authoritative' }
          : capability === 'workspace.settings.manage' || capability === 'workspace.members.manage'
            ? { state: 'allowed', reasonCode: 'workspace_platform_role' }
            : {})),
    });
    renderPage({ currentSnapshot });

    const workspaceAdmin = screen.getByRole('heading', { name: 'Workspace administration' }).closest('article')!;
    const graphAction = within(workspaceAdmin).getByRole('heading', { name: 'View activity' }).closest('li')!;
    const platformAction = within(workspaceAdmin).getAllByText('The API evaluated this action from the workspace role.')[0].closest('li')!;

    expect(within(graphAction).getByText('Unavailable')).toBeTruthy();
    expect(within(platformAction).getByText('Allowed')).toBeTruthy();
    expect(within(workspaceAdmin).getByRole('heading', { name: 'Read access' }).closest('section')?.textContent).toContain('Unavailable');
    expect(within(workspaceAdmin).getByRole('heading', { name: 'Write access' }).closest('section')?.textContent).toContain('Allowed');
  });

  it('does_not_present_assignments_as_verified_when_role_evidence_is_unavailable', () => {
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => decision(capability, capability === 'users.create'
        ? {
          state: 'pim_activation_required',
          reasonCode: 'directory_role_required',
          roleEvidence: {
            state: 'unavailable',
            requiredRoleTemplateIds: ['fe930be7-5e62-47db-91af-98c3a49a38b1'],
            assignments: [
              { roleTemplateId: 'custom-role-id', assignmentState: 'active', scope: 'tenant' },
              { roleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1', assignmentState: 'eligible', scope: 'tenant' },
            ],
          },
        }
        : {})),
    });
    renderPage({ currentSnapshot });

    const createAction = screen.getByText('The API reported that a qualifying Microsoft role is required.').closest('li')!;
    expect(within(createAction).getByText('Role assignment evidence is unavailable.')).toBeTruthy();
    expect(within(createAction).queryAllByText(/Active assignment|Eligible assignment/)).toHaveLength(0);
    expect(within(createAction).queryByText(/PIM eligibility is not an active role assignment/)).toBeNull();
    expect(within(createAction).queryAllByText(/custom-role-id/)).toHaveLength(0);
  });

  it('marks_retained_snapshot_stale_during_refresh_or_after_failure', () => {
    const { rerender } = renderPage({ currentSnapshot: snapshot(), error: true });
    expect(screen.getAllByText(/previous access check|stale/i).length).toBeGreaterThan(0);

    rerender(
      <AccessTransparencyProvider session={session} value={{ snapshot: snapshot(), loading: false, error: true, refresh: vi.fn() }}>
        <MyAccessPage session={session} />
      </AccessTransparencyProvider>,
    );
    expect(screen.getAllByText(/previous access check|stale/i).length).toBeGreaterThan(0);
  });

  it('renders_partial_missing_and_mixed_action_evidence_without_allowed_summary', () => {
    const currentSnapshot = snapshot({
      capabilities: capabilities.filter(item => item !== 'users.sessions.revoke').map(capability => decision(capability, capability === 'users.update'
        ? { state: 'consent_required', reasonCode: 'delegated_scope_required', missingScopes: ['User.ReadWrite.All'] }
        : capability === 'users.disable'
          ? { state: 'temporarily_unavailable', reasonCode: 'scope_probe_unavailable' }
          : {})),
    });
    renderPage({ currentSnapshot });

    const usersModule = screen.getByRole('heading', { name: 'Users' }).closest('article')!;
    const writeGroup = within(usersModule).getByRole('group', { name: 'Write access' });
    expect(within(writeGroup).getAllByText('Partial evidence').length).toBeGreaterThan(0);
    expect(within(writeGroup).getByText(/Edit users/)).toBeTruthy();
    expect(within(writeGroup).getByText(/The API reported these missing Microsoft scopes: User.ReadWrite.All/)).toBeTruthy();
    expect(within(writeGroup).getByText(/Microsoft authorization could not be verified/)).toBeTruthy();
    expect(within(writeGroup).getAllByText(/The API did not return a decision for this action/).length).toBeGreaterThan(0);
    expect(writeGroup.querySelector('.my-access-group__heading')?.textContent).toContain('Partial evidence');
    expect(writeGroup.querySelector('.my-access-group__heading')?.textContent).not.toContain('Allowed');
  });

  it('shows_active_and_eligible_roles_and_scope_separately', () => {
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => decision(capability, capability === 'users.create' ? {
        state: 'pim_activation_required',
        reasonCode: 'directory_role_required',
        requiredRoleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1',
        roleEvidence: {
          state: 'available',
          requiredRoleTemplateIds: ['fe930be7-5e62-47db-91af-98c3a49a38b1'],
          assignments: [
            { roleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1', assignmentState: 'eligible', scope: 'tenant', pimState: 'eligible' },
            { roleTemplateId: 'custom-role-id', assignmentState: 'active', scope: '/administrativeUnits/au-1' },
          ],
        },
        nextStep: { label: 'Open PIM guidance', href: '/identity' },
      } : {})),
    });
    renderPage({ currentSnapshot });

    expect(screen.getAllByText(/User Administrator/).length).toBeGreaterThan(0);
    expect(screen.getByText(/Eligible/)).toBeTruthy();
    expect(screen.getByText(/Active/)).toBeTruthy();
    expect(screen.getByText(/Scope reported: administrative unit/)).toBeTruthy();
    fireEvent.click(screen.getByText(/Technical details/i));
    expect(screen.getByText('custom-role-id')).toBeTruthy();
    expect(screen.getByText(/PIM eligibility is not an active role/)).toBeTruthy();
  });

  it('uses_only_supported_next_steps_and_never_submits_consent_or_activation', () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal('fetch', fetchSpy);
    const navigate = vi.fn();
    const currentSnapshot = snapshot({
      capabilities: capabilities.map(capability => decision(capability,
        capability === 'users.create'
          ? { state: 'consent_required', reasonCode: 'consent_required', missingScopes: ['User.ReadWrite.All'], nextStep: { label: 'Grant delegated consent', href: '/api/workspaces/current/consent/start' } }
          : capability === 'users.update'
            ? { state: 'pim_activation_required', reasonCode: 'directory_role_required', requiredRoleTemplateId: 'role-1', nextStep: { label: 'Unsupported handoff', href: 'https://evil.example/activate' } }
            : capability === 'users.reset_password'
              ? { state: 'pim_activation_required', reasonCode: 'directory_role_required', nextStep: { label: 'Open PIM guidance', href: '/identity' } }
            : capability === 'users.disable'
              ? { state: 'pim_activation_required', reasonCode: 'directory_role_required', requiredRoleTemplateId: 'fe930be7-5e62-47db-91af-98c3a49a38b1', nextStep: { label: 'Open Microsoft Entra PIM', href: 'https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade' } }
              : {})),
    });
    renderPage({ currentSnapshot, onNavigate: navigate });

    expect(navigate).not.toHaveBeenCalled();
    expect(screen.queryByRole('link', { name: /Microsoft permission setup/i })).toBeNull();
    expect(screen.getByText(/contact your workspace administrator/)).toBeTruthy();
    expect(screen.queryByRole('link', { name: 'Unsupported handoff' })).toBeNull();
    expect(screen.queryByRole('link', { name: 'Open PIM guidance' })).toBeNull();
    expect(screen.getByRole('link', { name: /Open Microsoft Entra PIM/ }).getAttribute('href')).toBe('https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade');
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
