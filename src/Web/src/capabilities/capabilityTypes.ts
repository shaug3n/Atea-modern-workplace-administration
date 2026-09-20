export type Capability =
  | 'users.view'
  | 'users.create'
  | 'users.update'
  | 'users.disable'
  | 'users.reset_password'
  | 'groups.manage_members'
  | 'licenses.assign'
  | 'roles.assign'
  | 'pim.view'
  | 'pim.activate'
  | 'workspace.settings.manage';

export const workspaceSettingsCapability: Capability = 'workspace.settings.manage';

export type CapabilityState =
  | 'allowed'
  | 'read_only'
  | 'hidden'
  | 'disabled'
  | 'consent_required'
  | 'pim_activation_required'
  | 'pim_approval_required'
  | 'pim_mfa_required'
  | 'pim_eligibility_expired'
  | 'temporarily_unavailable';

export type CapabilityDecision = {
  capability: Capability;
  state: CapabilityState;
  reasonCode: string;
  requiredRoleTemplateId?: string | null;
  pim?: { state: string; activationUrl?: string | null } | null;
  nextStep?: { label: string; href?: string | null } | null;
};

export type CapabilitySnapshot = {
  workspaceId: string;
  evaluatedAt: string;
  capabilities: CapabilityDecision[];
  sourceState?: string | null;
  sourceReasonCode?: string | null;
};
