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
