import type { ApiFetch } from './userDetailApi';

export type CreateUserCommand = {
  displayName: string;
  givenName: string;
  surname: string;
  userPrincipalName: string;
  mailNickname: string;
  jobTitle: string | null;
  department: string | null;
  officeLocation: string | null;
  mobilePhone: string | null;
  usageLocation: string;
  accountEnabled: boolean;
};

export type UpdateUserCommand = {
  displayName: string | null;
  givenName: string | null;
  surname: string | null;
  jobTitle: string | null;
  department: string | null;
  officeLocation: string | null;
  mobilePhone: string | null;
  usageLocation: string | null;
  accountEnabled: boolean | null;
};

export type LicenseAssignmentCommand = {
  skuId: string;
  disabledPlans: string[];
};

export type UserCommandResponse = {
  status: string;
  requiredCapability: string;
  replayed: boolean;
  error?: string | null;
  auditWarning?: string | null;
  temporaryCredentialNotice?: {
    temporaryPassword: string;
    forceChangePasswordNextSignIn: boolean;
  } | null;
};

export async function mutateUser(api: ApiFetch, path: string, method: 'POST' | 'PATCH' | 'DELETE', body: unknown, idempotencyKey = randomKey()) {
  const response = await api(path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    body: method === 'DELETE' ? (body === undefined ? undefined : JSON.stringify(body)) : JSON.stringify(body),
  });
  const payload = await response.json().catch(() => ({})) as UserCommandResponse;
  if (!response.ok && !payload.error) {
    throw new Error('user_mutation_failed');
  }

  return payload;
}

function randomKey() {
  return globalThis.crypto?.randomUUID?.() ?? `mutation-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
