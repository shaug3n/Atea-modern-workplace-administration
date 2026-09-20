export type ConnectionState = 'awaiting_invitation' | 'consent_required' | 'connected' | 'permission_incomplete' | 'temporarily_unavailable' | 'consent_revoked' | 'connection_failed';

export const messages = {
  appTitle: 'Atea Unified Workplace', enableDarkMode: 'Enable dark mode', disableDarkMode: 'Disable dark mode',
  homeLinkLabel: 'Atea Unified Workplace home', skipToContent: 'Skip to main content', shellLoading: 'Loading workspace shell…', shellUnavailable: 'Workspace shell is unavailable. Try again later.',
  primaryNavigationLabel: 'Primary navigation', tenantContextLabel: 'Current workspace and signed-in user', workspaceLabel: 'Workspace', signedInUserLabel: 'Signed-in user', signedInAs: 'Signed in as', unknownUser: 'Unknown user',
  navOverview: 'Overview', navUsers: 'Users', navLicenses: 'Licenses', navAudit: 'Audit activity', navWorkspaceSettings: 'Workspace settings',
  connectionFresh: 'Connection fresh', connectionNeedsCheck: 'Connection needs check', connectionFreshnessDetail: 'Capability snapshot',
  shellPreviewLabel: 'Preview route', usersTitle: 'Users', usersDescription: 'Directory users will appear here in the next MVP task.', userDetailTitle: 'User details', userDetailDescription: 'User profile, licenses, groups and roles will appear here when the directory flow is connected.',
  licensesTitle: 'Licenses', licensesDescription: 'License assignment workflows will appear here when Microsoft Graph license data is connected.', auditTitle: 'Audit activity', auditDescription: 'Workspace audit events will appear here when audit read endpoints are connected.',
  workspaceSettingsTitle: 'Workspace settings', workspaceSettingsDescription: 'Workspace connection, consent and onboarding settings will appear here for workspace administrators.', notFoundTitle: 'Page not found', notFoundDescription: 'Choose a section from the primary navigation.',
  permissionRequiredTitle: 'Permission required', permissionRequiredBody: 'This workspace section is not available for your current role or tenant state.',
  authSignInTitle: 'Sign in to Atea Unified Workplace', authSignIn: 'Sign in', authSignInError: 'Sign-in could not be started.', authSignInRequired: 'Sign-in is required',
  connectionTitle: 'Microsoft 365 connection', connectionCheck: 'Check connection', connectionConsent: 'Start consent', connectionContinueConsent: 'Continue consent', connectionCopyInvitation: 'Copy invitation instruction', connectionLoading: 'Loading connection status…', connectionUnavailable: 'Connection status is unavailable.', connectionActionFailed: 'That connection action could not be completed. Try again.',
  connectionStaleLabel: 'Connection check is stale', connectionStaleAction: 'Run a new connection check before using connected operations.',
  connectionState: {
    awaiting_invitation: { label: 'Invitation pending', action: 'Invite the customer administrator.' },
    consent_required: { label: 'Consent required', action: 'Ask the customer administrator to grant delegated consent.' },
    connected: { label: 'Connected', action: 'Connection is ready for supported operations.' },
    permission_incomplete: { label: 'Permissions incomplete', action: 'Grant the missing delegated permissions or tenant role.' },
    temporarily_unavailable: { label: 'Temporarily unavailable', action: 'Retry the connection check later.' },
    consent_revoked: { label: 'Consent revoked', action: 'Grant delegated consent again.' },
    connection_failed: { label: 'Connection failed', action: 'Review the handoff instructions and retry consent or connection verification.' },
  } satisfies Record<ConnectionState, { label: string; action: string }>,
} as const;
