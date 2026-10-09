import type { AppSession } from '../../components/TenantContextHeader';
import type { Capability, CapabilityDecision, CapabilitySnapshot, CapabilityState } from '../../capabilities/capabilityTypes';
import { myAccessMessages } from './messages';

export type AccessSummaryState = CapabilityState
  | 'mixed'
  | 'partial'
  | 'unavailable'
  | 'workspace_not_granted'
  | 'module_disabled'
  | 'not_applicable';

export type AccessModuleKey = 'users' | 'devices' | 'licenses' | 'workspace-administration' | 'exchange' | 'authentication-campaigns';

type AccessGroupDefinition = {
  key: AccessModuleKey;
  label: string;
  moduleGate: string | null;
  coverageComplete: boolean;
  read: readonly Capability[];
  write: readonly Capability[];
};

export const accessSummaryCapabilityGroups: readonly AccessGroupDefinition[] = [
  {
    key: 'users',
    label: 'Users',
    moduleGate: 'users',
    coverageComplete: true,
    read: ['users.view', 'authentication.methods.view', 'pim.view'],
    write: [
      'users.create',
      'users.update',
      'users.disable',
      'users.reset_password',
      'users.sessions.revoke',
      'groups.manage_members',
      'authentication.methods.manage',
      'roles.assign',
      'pim.activate',
    ],
  },
  {
    key: 'devices',
    label: 'Devices',
    moduleGate: 'devices',
    coverageComplete: true,
    read: ['devices.view', 'devices.bitlocker.metadata', 'devices.laps.metadata'],
    write: ['devices.manage', 'devices.privileged.manage', 'devices.bitlocker.reveal', 'devices.laps.reveal'],
  },
  {
    key: 'licenses',
    label: 'Licenses',
    moduleGate: 'licenses',
    coverageComplete: true,
    read: ['licenses.view'],
    write: ['licenses.assign'],
  },
  {
    key: 'workspace-administration',
    label: 'Workspace administration',
    moduleGate: null,
    coverageComplete: true,
    read: ['audit.view'],
    write: ['workspace.settings.manage', 'workspace.members.manage'],
  },
  {
    key: 'exchange',
    label: 'Exchange',
    moduleGate: 'exchange',
    coverageComplete: false,
    read: [],
    write: [],
  },
  {
    key: 'authentication-campaigns',
    label: 'Authentication campaigns',
    moduleGate: 'authentication-campaigns',
    coverageComplete: true,
    read: ['authentication.campaigns.view'],
    write: [],
  },
];

export type AccessActionSummary = {
  capability: Capability;
  state: AccessSummaryState;
  decision: CapabilityDecision | null;
  reasonLabel: string;
  consentEvidence: string | null;
};

export type AccessGroupSummary = {
  state: AccessSummaryState;
  workspaceGateState: AccessSummaryState | null;
  actions: AccessActionSummary[];
};

export type AccessModuleSummary = {
  key: AccessModuleKey;
  label: string;
  read: AccessGroupSummary;
  write: AccessGroupSummary;
};

export type AccessTransparencySummary = {
  workspaceId: string;
  evaluatedAt: string | null;
  sourceState: string | null;
  modules: AccessModuleSummary[];
};

const reasonLabels: Record<string, string> = {
  active_role: 'The API reported an active role for this action.',
  consent_required: 'Microsoft consent is required for this action.',
  delegated_scope_required: 'The API reported that delegated Microsoft permissions are required.',
  directory_read_required: 'The API reported that Microsoft directory read access is required.',
  directory_role_required: 'The API reported that a qualifying Microsoft role is required.',
  directory_role_scope_not_tenant_wide: 'The API reported that the role scope is not tenant-wide.',
  graph_authoritative: 'The API evaluated this action using Microsoft authorization evidence.',
  graph_snapshot_unavailable: 'The API could not evaluate Microsoft authorization evidence.',
  role_read_only: 'The API reported read-only access for this action.',
  scope_probe_unavailable: 'The API could not verify the required Microsoft permissions.',
  workspace_platform_role: 'The API evaluated this action from the workspace role.',
  workspace_platform_role_required: 'The API reported that a workspace role is required.',
};

function consentEvidence(decision: CapabilityDecision): string | null {
  if (decision.state !== 'consent_required') return null;
  if (decision.missingScopes === null || decision.missingScopes === undefined) {
    return myAccessMessages.myAccessConsentEvidenceUnavailable;
  }
  if (decision.missingScopes.length === 0) {
    return myAccessMessages.myAccessNoMissingScopes;
  }
  return myAccessMessages.myAccessMissingScopes.replace('{scopes}', decision.missingScopes.join(', '));
}

function decisionReason(decision: CapabilityDecision): string {
  return reasonLabels[decision.reasonCode]
    ?? `The API reported: ${decision.reasonCode.replace(/_/g, ' ')}.`;
}

function hasCompleteRoleEvidence(decision: CapabilityDecision): boolean {
  const roleState = decision.roleEvidence?.state;
  return roleState === 'available' || roleState === 'not_applicable';
}

function emptyAction(capability: Capability, state: AccessSummaryState, reasonLabel: string): AccessActionSummary {
  return { capability, state, decision: null, reasonLabel, consentEvidence: null };
}

function actionSummary(
  capability: Capability,
  decision: CapabilityDecision | undefined,
  state: AccessSummaryState,
): AccessActionSummary {
  if (!decision) return emptyAction(capability, 'partial', myAccessMessages.myAccessDecisionMissing);
  return {
    capability,
    state: state === 'unavailable' || state === 'module_disabled' || state === 'workspace_not_granted'
      ? state
      : hasCompleteRoleEvidence(decision) ? state : 'partial',
    decision,
    reasonLabel: decisionReason(decision),
    consentEvidence: consentEvidence(decision),
  };
}

function gatedGroup(
  capabilities: readonly Capability[],
  snapshot: CapabilitySnapshot,
  gateState: AccessSummaryState,
): AccessGroupSummary {
  return {
    state: gateState,
    workspaceGateState: gateState,
    actions: capabilities.map(capability => actionSummary(
      capability,
      snapshot.capabilities.find(item => item.capability === capability),
      gateState,
    )),
  };
}

function summarizeGroup(
  capabilities: readonly Capability[],
  snapshot: CapabilitySnapshot,
  moduleGate: string | null,
  coverageComplete: boolean,
): AccessGroupSummary {
  if (!coverageComplete) {
    return {
      state: 'unavailable',
      workspaceGateState: null,
      actions: capabilities.map(capability => emptyAction(capability, 'unavailable', myAccessMessages.myAccessCoverageUnavailable)),
    };
  }

  if (capabilities.length === 0) {
    return { state: 'not_applicable', workspaceGateState: null, actions: [] };
  }

  if (moduleGate) {
    const evidence = snapshot.workspaceModules?.find(item => item.module === moduleGate);
    if (!evidence) {
      return gatedGroup(capabilities, snapshot, 'unavailable');
    }
    if (!evidence.enabled) {
      return gatedGroup(capabilities, snapshot, 'module_disabled');
    }
    if (!evidence.effective) {
      return gatedGroup(capabilities, snapshot, 'workspace_not_granted');
    }
  }

  if (snapshot.sourceState !== 'graph_authoritative') {
    const actions = capabilities.map(capability => {
      const decision = snapshot.capabilities.find(item => item.capability === capability);
      if (!decision) return emptyAction(capability, 'partial', myAccessMessages.myAccessDecisionMissing);
      const isWorkspaceDecision = decision.reasonCode.startsWith('workspace_platform_');
      return actionSummary(
        capability,
        decision,
        isWorkspaceDecision ? decision.state : 'unavailable',
      );
    });
    if (actions.some(action => action.state === 'partial')) {
      return { state: 'partial', workspaceGateState: null, actions };
    }
    const states = new Set(actions.map(action => action.state));
    return {
      state: states.size > 1 ? 'mixed' : actions[0]?.state ?? 'unavailable',
      workspaceGateState: null,
      actions,
    };
  }

  const actions = capabilities.map(capability => {
    const decision = snapshot.capabilities.find(item => item.capability === capability);
    return actionSummary(capability, decision, decision?.state ?? 'partial');
  });

  if (actions.some(action => action.decision === null)) {
    return { state: 'partial', workspaceGateState: null, actions };
  }
  if (actions.some(action => action.state === 'partial')) {
    return { state: 'partial', workspaceGateState: null, actions };
  }

  const states = new Set(actions.map(action => action.state));
  if (states.size > 1) return { state: 'mixed', workspaceGateState: null, actions };
  return { state: actions[0].state, workspaceGateState: null, actions };
}

export function summarizeAccess(snapshot: CapabilitySnapshot | null, session: AppSession): AccessTransparencySummary {
  const base = {
    workspaceId: session.workspace.id,
    evaluatedAt: null as string | null,
    sourceState: null as string | null,
  };
  if (!snapshot || snapshot.workspaceId !== session.workspace.id) {
    return {
      ...base,
      modules: accessSummaryCapabilityGroups.map(group => ({
        key: group.key,
        label: group.label,
        read: {
          state: 'unavailable',
          workspaceGateState: null,
          actions: group.read.map(capability => emptyAction(capability, 'unavailable', myAccessMessages.myAccessSnapshotUnavailable)),
        },
        write: {
          state: 'unavailable',
          workspaceGateState: null,
          actions: group.write.map(capability => emptyAction(capability, 'unavailable', myAccessMessages.myAccessSnapshotUnavailable)),
        },
      })),
    };
  }

  return {
    workspaceId: snapshot.workspaceId,
    evaluatedAt: snapshot.evaluatedAt,
    sourceState: snapshot.sourceState ?? null,
    modules: accessSummaryCapabilityGroups.map(group => ({
      key: group.key,
      label: group.label,
      read: summarizeGroup(group.read, snapshot, group.moduleGate, group.coverageComplete),
      write: summarizeGroup(group.write, snapshot, group.moduleGate, group.coverageComplete),
    })),
  };
}
