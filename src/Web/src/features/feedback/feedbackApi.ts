import type { ApiFetch } from '../users/userDetailApi';

export type FeedbackCategory = 'Bug' | 'Improvement' | 'General';

export type FeedbackRequest = {
  category: FeedbackCategory;
  subject: string;
  message: string;
};

export type FeedbackSubmission = FeedbackRequest & {
  id: string;
  createdAt: string;
  expiresAt: string;
};

export type FeedbackPageResponse = {
  items: FeedbackSubmission[];
  nextCursor: string | null;
};

export class FeedbackApiError extends Error {
  constructor(public readonly status?: number, public readonly code?: string) {
    super('feedback_api_unavailable');
  }
}

function isFeedbackCategory(value: unknown): value is FeedbackCategory {
  return value === 'Bug' || value === 'Improvement' || value === 'General';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

async function parseJson(response: Response): Promise<unknown> {
  try {
    const value: unknown = await response.json();
    return value;
  } catch {
    throw new FeedbackApiError(response.status, 'invalid_response');
  }
}

function submissionFrom(value: unknown): FeedbackSubmission | null {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || !isFeedbackCategory(value.category)
    || typeof value.subject !== 'string'
    || typeof value.message !== 'string'
    || typeof value.createdAt !== 'string'
    || typeof value.expiresAt !== 'string') return null;
  return {
    id: value.id,
    category: value.category,
    subject: value.subject,
    message: value.message,
    createdAt: value.createdAt,
    expiresAt: value.expiresAt,
  };
}

export async function listFeedback(api: ApiFetch, cursor?: string): Promise<FeedbackPageResponse> {
  const path = cursor
    ? `/api/feedback/submissions?cursor=${encodeURIComponent(cursor)}`
    : '/api/feedback/submissions';
  let response: Response;
  try {
    response = await api(path, { cache: 'no-store' });
  } catch {
    throw new FeedbackApiError(undefined, 'network_error');
  }
  if (!response.ok) {
    const body = await parseJson(response);
    throw new FeedbackApiError(response.status, isRecord(body) && typeof body.error === 'string' ? body.error : undefined);
  }
  const body = await parseJson(response);
  if (!isRecord(body) || !Array.isArray(body.items)
    || !(body.nextCursor === null || typeof body.nextCursor === 'string')) {
    throw new FeedbackApiError(response.status, 'invalid_response');
  }
  const items = body.items.map(submissionFrom);
  if (items.some(item => item === null)) throw new FeedbackApiError(response.status, 'invalid_response');
  return { items: items.filter((item): item is FeedbackSubmission => item !== null), nextCursor: body.nextCursor };
}

export async function createFeedback(api: ApiFetch, request: FeedbackRequest, idempotencyKey: string): Promise<void> {
  let response: Response;
  try {
    response = await api('/api/feedback/submissions', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(request),
    });
  } catch {
    throw new FeedbackApiError(undefined, 'network_error');
  }
  const body = await parseJson(response);
  if (!response.ok) {
    throw new FeedbackApiError(response.status, isRecord(body) && typeof body.error === 'string' ? body.error : undefined);
  }
  if (!isRecord(body)
    || typeof body.id !== 'string'
    || typeof body.createdAt !== 'string'
    || typeof body.expiresAt !== 'string') {
    throw new FeedbackApiError(response.status, 'invalid_response');
  }
}
