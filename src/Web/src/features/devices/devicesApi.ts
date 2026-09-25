import type { ApiFetch } from '../users/userDetailApi';

export type ManagedDevice = {
  id: string;
  deviceName?: string | null;
  operatingSystem?: string | null;
  osVersion?: string | null;
  complianceState?: string | null;
  managementState?: string | null;
  managedDeviceOwnerType?: string | null;
  lastSyncDateTime?: string | null;
  userId?: string | null;
  azureAdDeviceId?: string | null;
  serialNumber?: string | null;
  manufacturer?: string | null;
  model?: string | null;
};

export type DevicesResponse = {
  items: ManagedDevice[];
  total: number;
  fetchedAt: string;
  freshness: string;
  partialData: boolean;
  access: { state: string; reasonCode?: string | null };
  continuationToken?: string | null;
  error?: { category: string; message: string } | null;
};

export type DeviceFilters = { search: string; complianceState: string; operatingSystem: string };
export type DeviceLoader = (filters: DeviceFilters, continuationToken?: string | null) => Promise<DevicesResponse>;
export type DeviceAction = 'sync' | 'remote-lock' | 'restart' | 'retire' | 'wipe';
export type RecoveryStatus = 'succeeded' | 'reason_required' | 'invalid_target' | 'device_not_found' | 'entra_device_missing' | 'recovery_not_found' | 'missing_scope' | 'consent_required' | 'graph_forbidden' | 'throttled' | 'temporarily_unavailable' | 'audit_unavailable';
export type RecoveryResponse<T> = { status: RecoveryStatus; data?: T | null; error?: string | null; guidance?: string | null; retryAfterSeconds?: number | null; graphCorrelationId?: string | null; graphRequestId?: string | null };
export type BitlockerMetadata = { id: string; deviceId?: string | null; createdDateTime?: string | null; volumeType?: string | null };
export type LapsMetadata = { id: string; deviceName?: string | null; lastBackupDateTime?: string | null; refreshDateTime?: string | null };
export type BitlockerSecret = { key: string };
export type LapsSecret = { accountName?: string | null; password: string; backupDateTime?: string | null };

export class RecoveryFailure extends Error {
  constructor(public readonly result: RecoveryResponse<never>) { super(result.status); }
}

export async function fetchDeviceDetail(api: ApiFetch, deviceId: string): Promise<ManagedDevice> {
  const response = await api(`/api/devices/${encodeURIComponent(deviceId)}`, { cache: 'no-store' });
  if (!response.ok) throw new RecoveryFailure(await response.json().catch(() => ({ status: 'temporarily_unavailable' })));
  return await response.json() as ManagedDevice;
}

async function recoveryRequest<T>(api: ApiFetch, path: string, init: RequestInit = {}): Promise<RecoveryResponse<T>> {
  const response = await api(path, { ...init, cache: 'no-store' });
  const body = await response.json().catch(() => ({ status: 'temporarily_unavailable' })) as RecoveryResponse<T>;
  if (!response.ok || body.status !== 'succeeded') throw new RecoveryFailure(body as RecoveryResponse<never>);
  return body;
}

export function fetchBitlockerMetadata(api: ApiFetch, deviceId: string) {
  return recoveryRequest<BitlockerMetadata[]>(api, `/api/devices/${encodeURIComponent(deviceId)}/recovery/bitlocker`);
}

export function fetchLapsMetadata(api: ApiFetch, deviceId: string) {
  return recoveryRequest<LapsMetadata>(api, `/api/devices/${encodeURIComponent(deviceId)}/recovery/laps`);
}

export function revealBitlocker(api: ApiFetch, deviceId: string, keyId: string, reason: string) {
  return recoveryRequest<BitlockerSecret>(api, `/api/devices/${encodeURIComponent(deviceId)}/recovery/bitlocker/${encodeURIComponent(keyId)}/reveal`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }),
  });
}

export function revealLaps(api: ApiFetch, deviceId: string, reason: string) {
  return recoveryRequest<LapsSecret>(api, `/api/devices/${encodeURIComponent(deviceId)}/recovery/laps/reveal`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }),
  });
}

export async function fetchDevices(api: ApiFetch, filters: DeviceFilters, continuationToken: string | null = null) {
  const params = new URLSearchParams();
  if (filters.search) params.set('search', filters.search);
  if (filters.complianceState) params.set('complianceState', filters.complianceState);
  if (filters.operatingSystem) params.set('operatingSystem', filters.operatingSystem);
  if (continuationToken) params.set('continuationToken', continuationToken);
  const suffix = params.toString();
  const response = await api(`/api/devices${suffix ? `?${suffix}` : ''}`);
  if (!response.ok) throw new Error('devices_unavailable');
  return await response.json() as DevicesResponse;
}

export async function executeDeviceAction(api: ApiFetch, deviceId: string, action: DeviceAction) {
  const response = await api(`/api/devices/${encodeURIComponent(deviceId)}/actions/${action}`, {
    method: 'POST',
    headers: { 'Idempotency-Key': globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}` },
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error ?? 'device_action_failed');
  return body as { status: string };
}
