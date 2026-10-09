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

const guidPattern = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;
const timestampPattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/i;

function isFeedbackCategory(value: unknown): value is FeedbackCategory {
  return value === 'Bug' || value === 'Improvement' || value === 'General';
}

function isFeedbackId(value: string): boolean {
  return guidPattern.test(value) && value !== '00000000-0000-0000-0000-000000000000';
}

function isFeedbackCursor(value: unknown): value is string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 128 || !/^[A-Za-z0-9_-]+$/.test(value) || value.length % 4 === 1) {
    return false;
  }
  try {
    const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
    const decoded = globalThis.atob(base64.padEnd(base64.length + ((4 - base64.length % 4) % 4), '='));
    const parts = /^([0-9]{1,19}):([0-9a-f]{32})$/i.exec(decoded);
    if (!parts) return false;
    const ticks = BigInt(parts[1]);
    return ticks <= 3155378975999999999n;
  } catch (error) {
    if (error instanceof DOMException && error.name === 'InvalidCharacterError') return false;
    throw error;
  }
}

function timestampValue(value: string): number | null {
  const parts = timestampPattern.exec(value);
  if (!parts) return null;
  const [year, month, day, hour, minute, second] = parts.slice(1).map(Number);
  const calendar = new Date(0);
  calendar.setUTCFullYear(year, month - 1, day);
  calendar.setUTCHours(hour, minute, second, 0);
  if (calendar.getUTCFullYear() !== year
    || calendar.getUTCMonth() !== month - 1
    || calendar.getUTCDate() !== day
    || calendar.getUTCHours() !== hour
    || calendar.getUTCMinutes() !== minute
    || calendar.getUTCSeconds() !== second) return null;
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? timestamp : null;
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
    || !isFeedbackId(value.id)
    || !isFeedbackCategory(value.category)
    || typeof value.subject !== 'string'
    || typeof value.message !== 'string'
    || typeof value.createdAt !== 'string'
    || typeof value.expiresAt !== 'string') return null;
  const createdAt = timestampValue(value.createdAt);
  const expiresAt = timestampValue(value.expiresAt);
  if (createdAt === null || expiresAt === null || expiresAt <= createdAt) return null;
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
    || body.items.length > 20
    || !(body.nextCursor === null || isFeedbackCursor(body.nextCursor))) {
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
  const createdAt = isRecord(body) && typeof body.createdAt === 'string' ? timestampValue(body.createdAt) : null;
  const expiresAt = isRecord(body) && typeof body.expiresAt === 'string' ? timestampValue(body.expiresAt) : null;
  if (!isRecord(body)
    || typeof body.id !== 'string'
    || !isFeedbackId(body.id)
    || createdAt === null
    || expiresAt === null
    || expiresAt <= createdAt) {
    throw new FeedbackApiError(response.status, 'invalid_response');
  }
}
