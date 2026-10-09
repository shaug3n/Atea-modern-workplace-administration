export type Capability =
  | 'users.view'
  | 'users.create'
  | 'users.update'
  | 'users.disable'
  | 'users.reset_password'
  | 'users.sessions.revoke'
  | 'groups.manage_members'
  | 'licenses.view'
  | 'licenses.assign'
  | 'roles.assign'
  | 'pim.view'
  | 'pim.activate'
  | 'devices.view'
  | 'devices.manage'
  | 'devices.privileged.manage'
  | 'devices.bitlocker.metadata'
  | 'devices.bitlocker.reveal'
  | 'devices.laps.metadata'
  | 'devices.laps.reveal'
  | 'authentication.methods.view'
  | 'authentication.methods.manage'
  | 'authentication.campaigns.view'
  | 'authentication.campaigns.manage'
  | 'licenses.hygiene.view'
  | 'platform.about.view'
  | 'feedback.submit'
  | 'audit.view'
  | 'workspace.settings.manage';

export const reservedCapabilities = [
  'authentication.campaigns.manage',
  'platform.about.view',
  'feedback.submit',
] as const satisfies readonly Capability[];

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
  missingScopes?: string[] | null;
};

export type CapabilitySnapshot = {
  workspaceId: string;
  evaluatedAt: string;
  capabilities: CapabilityDecision[];
  sourceState?: string | null;
  sourceReasonCode?: string | null;
};

export function isPimCapabilityState(state: CapabilityState) {
  return state === 'pim_activation_required'
    || state === 'pim_approval_required'
    || state === 'pim_mfa_required'
    || state === 'pim_eligibility_expired';
}
