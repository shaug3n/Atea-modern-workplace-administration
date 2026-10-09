import type { ApiFetch } from '../users/userDetailApi';

export type Device360ReadStatus =
  | 'succeeded'
  | 'partial'
  | 'unsupported'
  | 'no_reported_policies'
  | 'invalid_target'
  | 'device_not_found'
  | 'capability_required'
  | 'missing_scope'
  | 'consent_required'
  | 'graph_forbidden'
  | 'throttled'
  | 'temporarily_unavailable'
  | 'failed';

export type Device360ApiError = {
  category: string;
  message: string;
  state: string | null;
  statusCode: number | null;
  retryAfterSeconds: number | null;
} | null;

export type Device360Response<T> = {
  status: Device360ReadStatus;
  data: T | null;
  retrievedAt: string | null;
  partialData: boolean;
  error: Device360ApiError;
  retryAfterSeconds: number | null;
  graphCorrelationId: string | null;
  graphRequestId: string | null;
};

export type DeviceCompliancePolicyState = {
  id: string;
  displayName: string | null;
  state: string | null;
  platformType: string | null;
  settingCount: number | null;
  version: number | null;
};

export type DeviceConfigurationState = {
  id: string;
  displayName: string | null;
  state: string | null;
  platformType: string | null;
  settingCount: number | null;
  version: number | null;
};

export type DeviceConfigurationAssignmentTarget = {
  configurationId: string;
  configurationName: string | null;
  assignmentId: string;
  assignmentKind: string;
  targetType: string;
  groupId: string | null;
  filterId: string | null;
  filterType: string | null;
};

export type DeviceDetectedApp = {
  id: string;
  displayName: string | null;
  version: string | null;
  platform: string;
  publisher: string | null;
};

export type DeviceWindowsProtectionState = {
  antiMalwareVersion: string | null;
  controlledConfigurationEnabled: boolean | null;
  deviceState: string | null;
  engineVersion: string | null;
  fullScanOverdue: boolean | null;
  fullScanRequired: boolean | null;
  isVirtualMachine: boolean | null;
  lastFullScanDateTime: string | null;
  lastFullScanSignatureVersion: string | null;
  lastQuickScanDateTime: string | null;
  lastQuickScanSignatureVersion: string | null;
  lastReportedDateTime: string | null;
  malwareProtectionEnabled: boolean | null;
  networkInspectionSystemEnabled: boolean | null;
  productStatus: string | null;
  quickScanOverdue: boolean | null;
  realTimeProtectionEnabled: boolean | null;
  rebootRequired: boolean | null;
  signatureUpdateOverdue: boolean | null;
  signatureVersion: string | null;
  tamperProtectionEnabled: boolean | null;
};

const sectionFailure = <T>(message: string, statusCode: number | null = null): Device360Response<T> => ({
  status: 'failed',
  data: null,
  retrievedAt: null,
  partialData: false,
  error: { category: 'unavailable', message, state: null, statusCode, retryAfterSeconds: null },
  retryAfterSeconds: null,
  graphCorrelationId: null,
  graphRequestId: null,
});

async function readSection<T>(api: ApiFetch, path: string): Promise<Device360Response<T>> {
  let response: Response;
  try {
    response = await api(path, { cache: 'no-store' });
  } catch {
    return sectionFailure<T>('The section could not be reached.');
  }

  let body: Partial<Device360Response<T>>;
  try {
    body = await response.json() as Partial<Device360Response<T>>;
  } catch {
    return sectionFailure<T>('The section returned an unreadable response.', response.status);
  }

  const knownStatuses: Device360ReadStatus[] = [
    'succeeded', 'partial', 'unsupported', 'no_reported_policies', 'invalid_target', 'device_not_found',
    'capability_required', 'missing_scope', 'consent_required', 'graph_forbidden', 'throttled',
    'temporarily_unavailable', 'failed',
  ];
  if (!body.status || !knownStatuses.includes(body.status)) return sectionFailure<T>('The section returned an unknown response status.', response.status);

  return {
    status: body.status,
    data: body.data ?? null,
    retrievedAt: body.retrievedAt ?? null,
    partialData: body.partialData ?? false,
    error: body.error ?? null,
    retryAfterSeconds: body.retryAfterSeconds ?? body.error?.retryAfterSeconds ?? null,
    graphCorrelationId: body.graphCorrelationId ?? null,
    graphRequestId: body.graphRequestId ?? null,
  };
}

function deviceSection<T>(api: ApiFetch, managedDeviceId: string, suffix: string) {
  return readSection<T>(api, `/api/devices/${encodeURIComponent(managedDeviceId)}${suffix}`);
}

export function fetchDeviceCompliancePolicies(api: ApiFetch, managedDeviceId: string) {
  return deviceSection<DeviceCompliancePolicyState[]>(api, managedDeviceId, '/compliance-policies');
}

export function fetchDeviceReportedConfiguration(api: ApiFetch, managedDeviceId: string) {
  return deviceSection<DeviceConfigurationState[]>(api, managedDeviceId, '/configuration/reported');
}

export function fetchDeviceConfigurationAssignments(api: ApiFetch, managedDeviceId: string) {
  return deviceSection<DeviceConfigurationAssignmentTarget[]>(api, managedDeviceId, '/configuration/assignments');
}

export function fetchDeviceApps(api: ApiFetch, managedDeviceId: string) {
  return deviceSection<DeviceDetectedApp[]>(api, managedDeviceId, '/apps');
}

export function fetchDeviceProtection(api: ApiFetch, managedDeviceId: string) {
  return deviceSection<DeviceWindowsProtectionState>(api, managedDeviceId, '/protection');
}
