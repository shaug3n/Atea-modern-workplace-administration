import type { ApiFetch } from '../users/userDetailApi';

export type ApiBuildMetadata = {
  productVersion?: string | null;
  commit?: string | null;
  branch?: string | null;
};

export class AboutApiError extends Error {
  constructor(public readonly status?: number, public readonly code?: 'invalid_response') {
    super('system_versions_unavailable');
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isBuildMetadata(value: unknown): value is ApiBuildMetadata {
  if (!isRecord(value)) return false;
  return ['productVersion', 'commit', 'branch'].every(field => {
    const property = Object.getOwnPropertyDescriptor(value, field);
    return property === undefined
      || property.value === null
      || typeof property.value === 'string';
  });
}

export async function fetchApiBuildMetadata(api: ApiFetch): Promise<ApiBuildMetadata> {
  let response: Response;
  try {
    response = await api('/api/about/system-versions', { cache: 'no-store' });
  } catch {
    throw new AboutApiError();
  }

  if (!response.ok) {
    throw new AboutApiError(response.status);
  }

  let value: unknown;
  try {
    value = await response.json();
  } catch {
    throw new AboutApiError(undefined, 'invalid_response');
  }
  if (!isBuildMetadata(value)) throw new AboutApiError(undefined, 'invalid_response');
  return value;
}
