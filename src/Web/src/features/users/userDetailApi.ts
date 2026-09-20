import type { CapabilityDecision } from '../../capabilities/capabilityTypes';

export type UserDetails = {
  id: string;
  displayName: string | null;
  userPrincipalName: string | null;
  mail: string | null;
  accountEnabled: boolean | null;
  userType: string | null;
  givenName?: string | null;
  surname?: string | null;
  jobTitle?: string | null;
  department?: string | null;
  officeLocation?: string | null;
  mobilePhone?: string | null;
  usageLocation?: string | null;
  isReadOnly: boolean;
  sourceOfAuthority: string;
  sourceOfAuthorityReason?: string | null;
};

export type AssignedLicense = {
  skuId: string;
  skuPartNumber: string | null;
  displayName?: string | null;
};

export type GroupMembership = {
  id: string;
  displayName: string | null;
  mailNickname: string | null;
  securityEnabled: boolean | null;
  groupTypes: string[];
};

export type DirectoryRoleAssignment = {
  id: string;
  roleTemplateId: string;
  displayName: string | null;
  assignmentState: string;
  directoryScopeId: string | null;
};

export type PimActivationAction = {
  action: 'request_activation';
  href: string;
  method: 'POST';
  requiredCapability: 'pim.activate';
  requiresConfirmation: boolean;
};

export type PimEligibility = {
  id: string;
  roleTemplateId: string;
  roleDefinitionId?: string | null;
  displayName: string | null;
  status: string;
  requiredCapability: 'pim.activate';
  activationAvailable: boolean;
  requiresApproval: boolean;
  requiresMfa: boolean;
  requiresJustification: boolean;
  maximumDurationMinutes: number | null;
  directoryScopeId?: string | null;
  expiresAt?: string | null;
  activationAction: PimActivationAction | null;
};

export type SectionAccessState = {
  authorization: CapabilityDecision;
  fetchedAt: string;
  freshness: 'fresh' | 'stale' | 'unavailable';
  partialData: boolean;
  error?: {
    category: string;
    message: string;
    state?: string | null;
    statusCode?: number | null;
    retryAfterSeconds?: number | null;
  } | null;
};

export type UserDetailSection<T> = {
  access: SectionAccessState;
  items: T[];
};

export type UserDetailResponse = {
  access: SectionAccessState;
  user: UserDetails | null;
  licenses: UserDetailSection<AssignedLicense>;
  groups: UserDetailSection<GroupMembership>;
  roles: UserDetailSection<DirectoryRoleAssignment>;
  pim: UserDetailSection<PimEligibility>;
};

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

export async function fetchUserDetail(api: ApiFetch, userId: string) {
  const response = await api(`/api/users/${encodeURIComponent(userId)}`);
  if (response.status === 404) {
    throw new Error('user_not_found');
  }

  if (!response.ok) {
    throw new Error('user_detail_unavailable');
  }

  return await response.json() as UserDetailResponse;
}
