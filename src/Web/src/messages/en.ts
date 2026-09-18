export type ConnectionState = 'awaiting_invitation' | 'consent_required' | 'connected' | 'permission_incomplete' | 'temporarily_unavailable' | 'consent_revoked' | 'connection_failed';

export const messages = {
  appTitle: 'Atea Unified Workplace', enableDarkMode: 'Enable dark mode', disableDarkMode: 'Disable dark mode',
  authSignInTitle: 'Sign in to Atea Unified Workplace', authSignIn: 'Sign in', authSignInError: 'Sign-in could not be started.', authSignInRequired: 'Sign-in is required',
  connectionTitle: 'Microsoft 365 connection', connectionCheck: 'Check connection', connectionConsent: 'Start consent', connectionCopyInvitation: 'Copy invitation instruction', connectionLoading: 'Loading connection status…', connectionUnavailable: 'Connection status is unavailable.',
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
