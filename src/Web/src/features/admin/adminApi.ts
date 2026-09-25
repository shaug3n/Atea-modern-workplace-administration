export type WorkspaceSummary = { id: string; tenantId: string; displayName: string; connectionStatus: string };
export type Membership = { id: string; tenantObjectId: string; email: string; platformRole: string; isAteaOperator: boolean };
export type InvitationSummary = { id: string; email: string; displayName: string; role: string; expiresAt: string; redeemedAt: string | null; revokedAt: string | null };
export type WorkspaceAdminDetail = WorkspaceSummary & { lastVerifiedAt: string | null; connectionFailureCategory: string | null; memberships: Membership[]; invitations: InvitationSummary[] };
export type InvitationResult = { invitationUrl: string; expiresAt: string };
export type WorkspaceOnboardingResult = { workspace: WorkspaceSummary; invitationUrl: string; expiresAt: string };

const isObject = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;
const isString = (value: unknown): value is string => typeof value === 'string';
const isWorkspace = (value: unknown): value is WorkspaceSummary => isObject(value) && isString(value.id) && isString(value.tenantId) && isString(value.displayName) && isString(value.connectionStatus);
const isMembership = (value: unknown): value is Membership => isObject(value) && isString(value.id) && isString(value.tenantObjectId) && isString(value.email) && isString(value.platformRole) && typeof value.isAteaOperator === 'boolean';
const nullableString = (value: unknown) => value === null || isString(value);
const isInvitation = (value: unknown): value is InvitationSummary => isObject(value) && isString(value.id) && isString(value.email) && isString(value.displayName) && isString(value.role) && isString(value.expiresAt) && nullableString(value.redeemedAt) && nullableString(value.revokedAt);
const invalid = () => new Error('The platform returned an unexpected workspace response.');

function statusError(status: number): Error {
  if (status === 401) return new Error('Your admin session has expired. Sign in again.');
  if (status === 403) return new Error('Your current admin scope cannot view or manage this workspace.');
  if (status === 404) return new Error('This workspace was not found in your current admin scope.');
  if (status === 409) return new Error('That workspace or membership already exists.');
  if (status === 503) return new Error('The workspace platform is temporarily unavailable. Try again later.');
  return new Error('The platform request could not be completed. Try again later.');
}
async function request<T>(path: string, init: RequestInit | undefined, guard: (value: unknown) => value is T): Promise<T> {
  const response = await fetch(path, { ...init, credentials: 'include', ...(init?.body ? { headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) } } : {}) });
  if (!response.ok) throw statusError(response.status);
  const value: unknown = await response.json();
  if (!guard(value)) throw invalid();
  return value;
}
async function requestNoContent(path: string, init: RequestInit): Promise<void> {
  const response = await fetch(path, { ...init, credentials: 'include' });
  if (!response.ok) throw statusError(response.status);
}
const arrayOf = <T>(guard: (value: unknown) => value is T) => (value: unknown): value is T[] => Array.isArray(value) && value.every(guard);
const detailGuard = (value: unknown): value is WorkspaceAdminDetail => {
  if (!isObject(value) || !isWorkspace(value)) return false;
  const candidate = value as Record<string, unknown>;
  return (candidate.lastVerifiedAt === null || isString(candidate.lastVerifiedAt)) && (candidate.connectionFailureCategory === null || isString(candidate.connectionFailureCategory)) && Array.isArray(candidate.memberships) && candidate.memberships.every(isMembership) && Array.isArray(candidate.invitations) && candidate.invitations.every(isInvitation);
};
const onboardingGuard = (value: unknown): value is WorkspaceOnboardingResult => isObject(value) && isWorkspace(value.workspace) && isString(value.invitationUrl) && isString(value.expiresAt);

export const adminApi = {
  listWorkspaces: () => request('/api/platform/workspaces', undefined, arrayOf(isWorkspace)),
  createWorkspace: (input: { tenantId: string; displayName: string }) => request('/api/platform/workspaces', { method: 'POST', body: JSON.stringify(input) }, isWorkspace),
  onboardWorkspace: (input: { tenantId: string; displayName: string; adminUpn: string; adminDisplayName?: string }) => request('/api/platform/workspaces/onboard', { method: 'POST', body: JSON.stringify(input) }, onboardingGuard),
  getWorkspace: (workspaceId: string) => request(`/api/platform/workspaces/${workspaceId}`, undefined, detailGuard),
  addMembership: (workspaceId: string, input: { tenantObjectId: string; email: string; platformRole: string; isAteaOperator: boolean }) => request(`/api/platform/workspaces/${workspaceId}/memberships`, { method: 'POST', body: JSON.stringify(input) }, isMembership),
  createInvitation: (workspaceId: string, input: { email: string; displayName: string; expiresAt: string; approvedTenantObjectId?: string }) => request(`/api/platform/workspaces/${workspaceId}/invitations`, { method: 'POST', body: JSON.stringify(input) }, (value): value is InvitationResult => isObject(value) && isString(value.invitationUrl) && isString(value.expiresAt)),
  reissueInvitation: (workspaceId: string, invitationId: string) => request(`/api/platform/workspaces/${workspaceId}/invitations/${invitationId}/reissue`, { method: 'POST' }, (value): value is InvitationResult => isObject(value) && isString(value.invitationUrl) && isString(value.expiresAt)),
  revokeInvitation: (workspaceId: string, invitationId: string) => requestNoContent(`/api/platform/workspaces/${workspaceId}/invitations/${invitationId}`, { method: 'DELETE' }),
};
