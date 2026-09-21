export type UserFiltersState = {
  search: string;
  accountStatus: string;
  tenantRole: string;
  license: string;
  userType: string;
};

export type UserSummary = {
  id: string;
  displayName: string | null;
  userPrincipalName: string | null;
  mail: string | null;
  accountEnabled: boolean | null;
  userType: string | null;
};

export type UsersDirectoryResponse = {
  items: UserSummary[];
  continuationToken: string | null;
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

export type ApiFetch = (path: string, init?: RequestInit) => Promise<Response>;

export async function fetchUsers(api: ApiFetch, filters: UserFiltersState, continuationToken: string | null, pageSize = 25) {
  const parameters = new URLSearchParams();
  parameters.set('pageSize', String(pageSize));
  if (continuationToken) parameters.set('continuationToken', continuationToken);
  if (filters.search.trim()) parameters.set('search', filters.search.trim());
  if (filters.accountStatus) parameters.set('accountStatus', filters.accountStatus);
  if (filters.userType) parameters.set('userType', filters.userType);
  if (filters.tenantRole) parameters.set('tenantRole', filters.tenantRole);
  if (filters.license) parameters.set('license', filters.license);

  const response = await api(`/api/users?${parameters.toString()}`);
  if (!response.ok) {
    throw new Error('users_unavailable');
  }

  return await response.json() as UsersDirectoryResponse;
}
