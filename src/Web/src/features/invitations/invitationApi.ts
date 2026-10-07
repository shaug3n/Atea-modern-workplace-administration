export type InvitationRedemption = { status: string; workspaceId: string; workspaceName: string; nextStep: string };
export type InvitationPreview = {
  workspaceName: string;
  expiresAt: string;
  flow: 'consent_first' | 'sign_in';
  permissionScopes: string[];
};
export type InvitationConsentStart = {
  authorizationUrl: string;
  scopes: string[];
  challenge: string;
  correlationId: string;
  expiresAt: string;
};
export type InvitationConsentResume = {
  valid: boolean;
  status: 'ready_to_sign_in' | 'consent_denied' | 'invalid_callback';
  tenantId?: string;
  correlationId: string;
};

async function requestInvitationJson<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, {
      ...init,
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', ...(init?.headers ?? {}) },
    });
  } catch {
    throw new Error('invitation_service_unavailable');
  }
  if (!response.ok) {
    if (response.status === 404) throw new Error('invitation_unavailable');
    if (response.status === 409) throw new Error('invitation_consent_not_available');
    throw new Error('invitation_service_unavailable');
  }
  try {
    return await response.json() as T;
  } catch {
    throw new Error('invitation_service_unavailable');
  }
}

export async function fetchInvitationPreview(nonce: string): Promise<InvitationPreview> {
  const value = await requestInvitationJson<Partial<InvitationPreview>>(
    `/api/invitations/${encodeURIComponent(nonce)}/preview`,
  );
  if (
    typeof value.workspaceName !== 'string' ||
    typeof value.expiresAt !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAt)) ||
    (value.flow !== 'consent_first' && value.flow !== 'sign_in') ||
    !Array.isArray(value.permissionScopes) ||
    value.permissionScopes.some(scope => typeof scope !== 'string')
  ) {
    throw new Error('invitation_service_unavailable');
  }
  return value as InvitationPreview;
}

export async function startInvitationConsent(nonce: string): Promise<InvitationConsentStart> {
  const value = await requestInvitationJson<Partial<InvitationConsentStart>>(
    `/api/invitations/${encodeURIComponent(nonce)}/consent/start`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' },
  );
  if (
    typeof value.authorizationUrl !== 'string' ||
    typeof value.challenge !== 'string' ||
    typeof value.correlationId !== 'string' ||
    typeof value.expiresAt !== 'string' ||
    !Number.isFinite(Date.parse(value.expiresAt)) ||
    !Array.isArray(value.scopes) ||
    value.scopes.some(scope => typeof scope !== 'string')
  ) {
    throw new Error('invitation_service_unavailable');
  }
  const consentUrl = new URL(value.authorizationUrl);
  if (
    consentUrl.protocol !== 'https:' ||
    consentUrl.hostname !== 'login.microsoftonline.com' ||
    !/^\/[0-9a-f-]{36}\/v2\.0\/adminconsent$/i.test(consentUrl.pathname) ||
    consentUrl.searchParams.get('state') !== value.challenge
  ) {
    throw new Error('invitation_service_unavailable');
  }
  return value as InvitationConsentStart;
}

export async function resumeInvitationConsent(
  nonce: string,
  state: string,
  tenant?: string,
  errorCode?: string,
): Promise<InvitationConsentResume> {
  const value = await requestInvitationJson<Partial<InvitationConsentResume>>(
    `/api/invitations/${encodeURIComponent(nonce)}/consent/resume`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ state, ...(tenant ? { tenant } : {}), ...(errorCode ? { errorCode } : {}) }),
    },
  );
  if (
    typeof value.valid !== 'boolean' ||
    !['ready_to_sign_in', 'consent_denied', 'invalid_callback'].includes(value.status ?? '') ||
    typeof value.correlationId !== 'string' ||
    (value.tenantId !== undefined && typeof value.tenantId !== 'string')
  ) {
    throw new Error('invitation_service_unavailable');
  }
  return value as InvitationConsentResume;
}

export async function redeemInvitation(nonce: string, getApiToken: () => Promise<string>): Promise<InvitationRedemption> {
  const token = await getApiToken();
  const response = await fetch(`/api/invitations/${encodeURIComponent(nonce)}/redeem`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` }
  });
  if (!response.ok) throw new Error(response.status === 400 ? 'invitation_invalid_or_expired' : 'invitation_redemption_failed');
  const value = await response.json() as Partial<InvitationRedemption>;
  if (typeof value.status !== 'string' || typeof value.workspaceId !== 'string' || typeof value.workspaceName !== 'string' || typeof value.nextStep !== 'string') throw new Error('invitation_redemption_failed');
  return value as InvitationRedemption;
}
