import type { ApiFetch } from './userDetailApi';

export type AuthenticationMethod = {
  id: string;
  type: string;
  displayName: string;
  createdDateTime?: string | null;
  model?: string | null;
  attestationLevel?: string | null;
};

export type AuthenticationMethodsResponse = {
  userObjectId: string;
  items: AuthenticationMethod[];
  fetchedAt: string;
  freshness: string;
  partialData: boolean;
  access: { state: string; reasonCode?: string | null };
  error?: { category: string; message: string } | null;
};

export async function fetchAuthenticationMethods(api: ApiFetch, userId: string) {
  const response = await api(`/api/users/${encodeURIComponent(userId)}/authentication-methods`);
  if (!response.ok) throw new Error('authentication_methods_unavailable');
  return await response.json() as AuthenticationMethodsResponse;
}

export async function removeAuthenticationMethod(api: ApiFetch, userId: string, method: AuthenticationMethod, reason: string) {
  const response = await api(`/api/users/${encodeURIComponent(userId)}/authentication-methods/${encodeURIComponent(method.id)}?type=${encodeURIComponent(method.type)}`, {
    method: 'DELETE',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`,
    },
    body: JSON.stringify({ reason }),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error ?? 'authentication_method_remove_failed');
  return body as { status: string; auditWarning?: string | null };
}

export async function resetAuthenticationMethods(api: ApiFetch, userId: string, reason: string) {
  const response = await api(`/api/users/${encodeURIComponent(userId)}/authentication-methods/reset-mfa`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`,
    },
    body: JSON.stringify({ reason }),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error ?? 'authentication_methods_reset_failed');
  return body as { status: string; removedCount: number; auditWarning?: string | null };
}

export type TemporaryAccessPassResponse = { status: string; temporaryAccessPass?: string | null; replayed?: boolean; auditWarning?: string | null };

export async function grantTemporaryAccessPass(api: ApiFetch, userId: string, reason: string) {
  const response = await api(`/api/users/${encodeURIComponent(userId)}/authentication-methods/temporary-access-pass`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`,
    },
    body: JSON.stringify({ reason }),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error ?? 'temporary_access_pass_failed');
  return body as TemporaryAccessPassResponse;
}
