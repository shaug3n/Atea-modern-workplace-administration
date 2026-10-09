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

  try {
    return await response.json() as ApiBuildMetadata;
  } catch {
    throw new AboutApiError(undefined, 'invalid_response');
  }
}
