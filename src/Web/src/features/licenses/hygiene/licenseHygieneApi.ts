import type { ApiFetch } from '../../users/usersApi';

export type LicenseHygieneSourceStatus = {
  freshness: string;
  partialData: boolean;
  fetchedAt: string | null;
  error: { category: string; message: string; statusCode?: number | null; retryAfterSeconds?: number | null } | null;
};

export type LicenseHygieneSku = {
  skuId: string;
  partNumber: string;
  displayName: string;
  purchased: number;
  assigned: number;
  available: number;
};

export type LicenseHygieneAssignedSku = {
  skuId: string;
  partNumber: string | null;
  displayName: string | null;
};

export type LicenseHygieneDisabledAccount = {
  id: string;
  displayName: string | null;
  userPrincipalName: string | null;
  assignedLicenses: LicenseHygieneAssignedSku[];
  evidenceAt: string;
  evidenceSource: string;
};

export type LicenseHygieneResponse = {
  access: { state: string; reasonCode?: string | null };
  inventory: LicenseHygieneSourceStatus;
  userEvidence: LicenseHygieneSourceStatus;
  coverage: { recordsAssessed: number; completed: boolean; stopReason: string; missingEvidenceRecords: number };
  capacityItems: LicenseHygieneSku[];
  disabledAccounts: LicenseHygieneDisabledAccount[];
};

export type LicenseHygieneLoader = (signal?: AbortSignal) => Promise<LicenseHygieneResponse>;

export async function fetchLicenseHygiene(api: ApiFetch, signal?: AbortSignal): Promise<LicenseHygieneResponse> {
  const response = await api('/api/licenses/hygiene', { cache: 'no-store', signal });
  if (!response.ok) {
    throw Object.assign(new Error('License hygiene data is unavailable.'), { status: response.status });
  }
  return await response.json() as LicenseHygieneResponse;
}
