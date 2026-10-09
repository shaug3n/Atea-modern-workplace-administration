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

const knownStatuses = new Set<string>([
  'succeeded', 'partial', 'unsupported', 'no_reported_policies', 'invalid_target', 'device_not_found',
  'capability_required', 'missing_scope', 'consent_required', 'graph_forbidden', 'throttled',
  'temporarily_unavailable', 'failed',
]);

function isJsonObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isDevice360ReadStatus(value: unknown): value is Device360ReadStatus {
  return typeof value === 'string' && knownStatuses.has(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isFinite(value));
}

function isNullableBoolean(value: unknown): value is boolean | null {
  return value === null || typeof value === 'boolean';
}

function isCompliancePolicy(value: unknown): value is DeviceCompliancePolicyState {
  return isJsonObject(value)
    && typeof value.id === 'string'
    && isNullableString(value.displayName)
    && isNullableString(value.state)
    && isNullableString(value.platformType)
    && isNullableNumber(value.settingCount)
    && isNullableNumber(value.version);
}

function isCompliancePolicyList(value: unknown): value is DeviceCompliancePolicyState[] {
  return Array.isArray(value) && value.every(isCompliancePolicy);
}

function isConfigurationState(value: unknown): value is DeviceConfigurationState {
  return isCompliancePolicy(value);
}

function isConfigurationStateList(value: unknown): value is DeviceConfigurationState[] {
  return Array.isArray(value) && value.every(isConfigurationState);
}

function isConfigurationAssignment(value: unknown): value is DeviceConfigurationAssignmentTarget {
  return isJsonObject(value)
    && typeof value.configurationId === 'string'
    && isNullableString(value.configurationName)
    && typeof value.assignmentId === 'string'
    && typeof value.assignmentKind === 'string'
    && typeof value.targetType === 'string'
    && isNullableString(value.groupId)
    && isNullableString(value.filterId)
    && isNullableString(value.filterType);
}

function isConfigurationAssignmentList(value: unknown): value is DeviceConfigurationAssignmentTarget[] {
  return Array.isArray(value) && value.every(isConfigurationAssignment);
}

function isDetectedApp(value: unknown): value is DeviceDetectedApp {
  return isJsonObject(value)
    && typeof value.id === 'string'
    && isNullableString(value.displayName)
    && isNullableString(value.version)
    && typeof value.platform === 'string'
    && isNullableString(value.publisher);
}

function isDetectedAppList(value: unknown): value is DeviceDetectedApp[] {
  return Array.isArray(value) && value.every(isDetectedApp);
}

function isWindowsProtectionState(value: unknown): value is DeviceWindowsProtectionState {
  return isJsonObject(value)
    && isNullableString(value.antiMalwareVersion)
    && isNullableBoolean(value.controlledConfigurationEnabled)
    && isNullableString(value.deviceState)
    && isNullableString(value.engineVersion)
    && isNullableBoolean(value.fullScanOverdue)
    && isNullableBoolean(value.fullScanRequired)
    && isNullableBoolean(value.isVirtualMachine)
    && isNullableString(value.lastFullScanDateTime)
    && isNullableString(value.lastFullScanSignatureVersion)
    && isNullableString(value.lastQuickScanDateTime)
    && isNullableString(value.lastQuickScanSignatureVersion)
    && isNullableString(value.lastReportedDateTime)
    && isNullableBoolean(value.malwareProtectionEnabled)
    && isNullableBoolean(value.networkInspectionSystemEnabled)
    && isNullableString(value.productStatus)
    && isNullableBoolean(value.quickScanOverdue)
    && isNullableBoolean(value.realTimeProtectionEnabled)
    && isNullableBoolean(value.rebootRequired)
    && isNullableBoolean(value.signatureUpdateOverdue)
    && isNullableString(value.signatureVersion)
    && isNullableBoolean(value.tamperProtectionEnabled);
}

function parseError(value: unknown): { valid: true; error: Device360ApiError } | { valid: false } {
  if (value === undefined || value === null) return { valid: true, error: null };
  if (!isJsonObject(value)
    || typeof value.category !== 'string'
    || typeof value.message !== 'string'
    || (value.state !== undefined && !isNullableString(value.state))
    || (value.statusCode !== undefined && !isNullableNumber(value.statusCode))
    || (value.retryAfterSeconds !== undefined && !isNullableNumber(value.retryAfterSeconds))) {
    return { valid: false };
  }

  return {
    valid: true,
    error: {
      category: value.category,
      message: value.message,
      state: value.state ?? null,
      statusCode: value.statusCode ?? null,
      retryAfterSeconds: value.retryAfterSeconds ?? null,
    },
  };
}

async function readSection<T>(api: ApiFetch, path: string, isData: (value: unknown) => value is T): Promise<Device360Response<T>> {
  let response: Response;
  try {
    response = await api(path, { cache: 'no-store' });
  } catch {
    return sectionFailure<T>('The section could not be reached.');
  }

  let parsed: unknown;
  try {
    parsed = await response.json();
  } catch {
    return sectionFailure<T>('The section returned an unreadable response.', response.status);
  }

  if (!isJsonObject(parsed)) return sectionFailure<T>('The section returned an unreadable response shape.', response.status);

  const status = parsed.status;
  const data = parsed.data;
  const retrievedAt = parsed.retrievedAt;
  const partialData = parsed.partialData;
  const retryAfterSeconds = parsed.retryAfterSeconds;
  const graphCorrelationId = parsed.graphCorrelationId;
  const graphRequestId = parsed.graphRequestId;
  const error = parseError(parsed.error);
  if (!isDevice360ReadStatus(status)
    || !Object.prototype.hasOwnProperty.call(parsed, 'data')
    || (data !== undefined && data !== null && !isData(data))
    || (retrievedAt !== undefined && !isNullableString(retrievedAt))
    || (partialData !== undefined && typeof partialData !== 'boolean')
    || (retryAfterSeconds !== undefined && !isNullableNumber(retryAfterSeconds))
    || (graphCorrelationId !== undefined && !isNullableString(graphCorrelationId))
    || (graphRequestId !== undefined && !isNullableString(graphRequestId))
    || !error.valid) {
    return sectionFailure<T>('The section returned an unknown or malformed response shape.', response.status);
  }

  return {
    status,
    data: data ?? null,
    retrievedAt: retrievedAt ?? null,
    partialData: partialData ?? false,
    error: error.error,
    retryAfterSeconds: retryAfterSeconds ?? error.error?.retryAfterSeconds ?? null,
    graphCorrelationId: graphCorrelationId ?? null,
    graphRequestId: graphRequestId ?? null,
  };
}

function deviceSection<T>(api: ApiFetch, managedDeviceId: string, suffix: string, isData: (value: unknown) => value is T) {
  return readSection(api, `/api/devices/${encodeURIComponent(managedDeviceId)}${suffix}`, isData);
}

export function fetchDeviceCompliancePolicies(api: ApiFetch, managedDeviceId: string) {
  return deviceSection(api, managedDeviceId, '/compliance-policies', isCompliancePolicyList);
}

export function fetchDeviceReportedConfiguration(api: ApiFetch, managedDeviceId: string) {
  return deviceSection(api, managedDeviceId, '/configuration/reported', isConfigurationStateList);
}

export function fetchDeviceConfigurationAssignments(api: ApiFetch, managedDeviceId: string) {
  return deviceSection(api, managedDeviceId, '/configuration/assignments', isConfigurationAssignmentList);
}

export function fetchDeviceApps(api: ApiFetch, managedDeviceId: string) {
  return deviceSection(api, managedDeviceId, '/apps', isDetectedAppList);
}

export function fetchDeviceProtection(api: ApiFetch, managedDeviceId: string) {
  return deviceSection(api, managedDeviceId, '/protection', isWindowsProtectionState);
}
