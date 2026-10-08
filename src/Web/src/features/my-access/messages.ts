export const myAccessMessages = {
  myAccessSnapshotUnavailable: 'Access evidence is unavailable for this workspace.',
  myAccessCoverageUnavailable: 'Microsoft action coverage is not available for this module.',
  myAccessGraphUnavailable: 'Microsoft authorization evidence is unavailable.',
  myAccessWorkspaceEvidenceUnavailable: 'Workspace module access evidence is unavailable.',
  myAccessModuleDisabled: 'This module is disabled in the workspace.',
  myAccessWorkspaceNotGranted: 'The workspace has not granted access to this module.',
  myAccessDecisionMissing: 'The API did not return a decision for this action.',
  myAccessConsentEvidenceUnavailable: 'Microsoft consent evidence was not provided separately.',
  myAccessNoMissingScopes: 'The API reported no missing scopes; complete consent is not separately shown.',
  myAccessMissingScopes: 'The API reported these missing Microsoft scopes: {scopes}.',
  myAccessProviderRequired: 'useAccessTransparency must be used within AccessTransparencyProvider.',
  myAccessLoadFailed: 'Access information could not be loaded.',
} as const;
