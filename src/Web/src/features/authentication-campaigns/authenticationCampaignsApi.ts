import type { ApiFetch } from '../users/usersApi';

export type AuthenticationCampaignsResponse = {
  items: AuthenticationCampaignsRegistration[];
  fetchedAt: string;
  sourceLastUpdatedFrom: string | null;
  sourceLastUpdatedTo: string | null;
  partialData: boolean;
  observedRecordCount: number;
  duplicateRecordCount: number;
  directoryEnrichmentState: string;
  enrichedAccountCount: number;
  reportErrorCategory: string | null;
  directoryErrorCategory: string | null;
};

export type AuthenticationCampaignsRegistration = {
  id: string;
  displayName: string | null;
  userPrincipalName: string | null;
  userType: string | null;
  methodsRegistered: string[] | null;
  isMfaRegistered: boolean | null;
  isMfaCapable: boolean | null;
  isPasswordlessCapable: boolean | null;
  isSystemPreferredAuthenticationMethodEnabled: boolean | null;
  systemPreferredAuthenticationMethods: string[] | null;
  userPreferredMethodForSecondaryAuthentication: string | null;
  lastUpdatedDateTime: string | null;
  passkeyRegistrationState: string;
  isGenericFido2Registered: boolean | null;
  phoneRegistrationState: string;
  phonePreferenceState: string;
  department: string | null;
  officeLocation: string | null;
  companyName: string | null;
  directoryJoinState: string;
};

const safeCategories = new Set(['forbidden', 'consent_required', 'invalid_response', 'unavailable']);

export class AuthenticationCampaignsApiError extends Error {
  constructor(readonly status: number, readonly category: string) {
    super('Authentication campaign registrations are unavailable.');
    this.name = 'AuthenticationCampaignsApiError';
  }
}

export async function fetchAuthenticationCampaigns(api: ApiFetch): Promise<AuthenticationCampaignsResponse> {
  const response = await api('/api/authentication-campaigns/registrations', { method: 'GET' });
  if (!response.ok) {
    let category = 'unavailable';
    try {
      const body = await response.json() as { error?: { category?: unknown } };
      if (typeof body.error?.category === 'string' && safeCategories.has(body.error.category)) {
        category = body.error.category;
      }
    } catch {
      // Keep the generic source category when the response has no safe problem body.
    }
    throw new AuthenticationCampaignsApiError(response.status, category);
  }

  return await response.json() as AuthenticationCampaignsResponse;
}
