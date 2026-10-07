import type { Capability } from '../capabilities/capabilityTypes';
import type { StatusTone } from '../components/StatusBadge';

export const CAPABILITY_LABELS: Record<Capability, string> = {
  'users.view': 'View users',
  'users.create': 'Create users',
  'users.update': 'Edit users',
  'users.disable': 'Disable users',
  'users.reset_password': 'Reset passwords',
  'users.sessions.revoke': 'Revoke sessions',
  'groups.manage_members': 'Manage group members',
  'licenses.view': 'View licenses',
  'licenses.assign': 'Assign licenses',
  'roles.assign': 'Assign roles',
  'pim.view': 'View PIM roles',
  'pim.activate': 'Activate PIM roles',
  'devices.view': 'View devices',
  'devices.manage': 'Manage devices',
  'devices.privileged.manage': 'Manage devices (remote actions)',
  'devices.bitlocker.metadata': 'View BitLocker key details',
  'devices.bitlocker.reveal': 'Reveal BitLocker keys',
  'devices.laps.metadata': 'View local admin password details',
  'devices.laps.reveal': 'Reveal local admin passwords',
  'authentication.methods.view': 'View sign-in methods',
  'authentication.methods.manage': 'Manage sign-in methods',
  'audit.view': 'View activity',
  'workspace.settings.manage': 'Manage workspace settings'
};

export function humanizeCapability(key: string): string {
  return Object.prototype.hasOwnProperty.call(CAPABILITY_LABELS, key) ? CAPABILITY_LABELS[key as Capability] : 'Additional permission';
}

const AUTH_METHOD_LABELS: Record<string, string> = {
  passwordauthenticationmethod: 'Password',
  microsoftauthenticatorauthenticationmethod: 'Microsoft Authenticator',
  phoneauthenticationmethod: 'Phone',
  fido2authenticationmethod: 'Passkey (FIDO2)',
  windowshelloforbusinessauthenticationmethod: 'Windows Hello for Business',
  emailauthenticationmethod: 'Email',
  softwareoathauthenticationmethod: 'Software OATH token',
  temporaryaccesspassauthenticationmethod: 'Temporary Access Pass',
  platformcredentialauthenticationmethod: 'Platform credential'
};

export function humanizeAuthMethodType(type?: string | null): string {
  if (!type) return 'Other method';
  const key = type.replace(/^#?microsoft\.graph\./i, '').toLowerCase();
  return AUTH_METHOD_LABELS[key] ?? 'Other method';
}

function sentenceCase(value: string): string {
  const text = value.replace(/[._]+/g, ' ').trim().toLowerCase();
  return text ? text.charAt(0).toUpperCase() + text.slice(1) : '';
}

export function humanizeAssignmentState(state?: string | null): { label: string; tone: StatusTone } {
  const key = (state ?? '').toLowerCase();
  if (key === 'active') return { label: 'Active', tone: 'success' };
  if (key === 'eligible') return { label: 'Eligible', tone: 'info' };
  return { label: sentenceCase(key) || 'Unknown', tone: 'neutral' };
}

const ACTIVITY_LABELS: Record<string, string> = {
  'users.create': 'Created user',
  'users.update': 'Updated user',
  'users.disable': 'Disabled user',
  'users.reactivate': 'Enabled user',
  'users.reset_password': 'Reset password',
  'users.sessions.revoke': 'Revoked sessions',
  'users.groups.add': 'Added user to group',
  'users.groups.remove': 'Removed user from group',
  'users.licenses.assign': 'Assigned license',
  'users.licenses.remove': 'Removed license',
  'users.authentication_methods.remove': 'Removed sign-in method',
  'users.authentication_methods.reset_mfa': 'Reset MFA',
  'users.authentication_methods.temporary_access_pass': 'Issued Temporary Access Pass',
  'users.csv': 'Exported users',
  'licenses.csv': 'Exported licenses',
  'devices.csv': 'Exported devices',
  'devices.recovery.reveal': 'Revealed device recovery data',
  'workspace.onboarded': 'Connected workspace',
  'workspace.modules.changed': 'Changed modules',
  'workspace.settings.manage': 'Changed workspace settings',
  'workspace.invitation.created': 'Created invitation',
  'workspace.invitation.redeemed': 'Redeemed invitation',
  'workspace.invitation.reissued': 'Reissued invitation',
  'workspace.invitation.revoked': 'Revoked invitation',
  'workspace.membership.added': 'Added member',
  'workspace.membership.removed': 'Removed member',
  'workspace.membership.role_changed': 'Changed member role',
  'workspace.membership.modules_changed': 'Changed member modules',
  'workspace.ownership.transferred': 'Transferred ownership'
};

export function humanizeActivityAction(key?: string | null): string {
  if (!key) return '—';
  return ACTIVITY_LABELS[key] ?? sentenceCase(key);
}
