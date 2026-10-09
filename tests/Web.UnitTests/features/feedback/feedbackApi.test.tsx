import { describe, expect, it } from 'vitest';
import { FeedbackApiError, listFeedback } from '../../../../src/Web/src/features/feedback/feedbackApi';

const validItem = {
  id: '11111111-1111-4111-8111-111111111111',
  category: 'General',
  subject: 'Subject',
  message: 'Message',
  createdAt: '2026-10-09T08:00:00Z',
  expiresAt: '2027-01-07T08:00:00Z',
};
const validCursor = 'NjM5MzI1OTI4MDAwMDAwMDAwOjExMTExMTExMTExMTQxMTE4MTExMTExMTExMTExMTEx';

function apiResponse(body: unknown) {
  return async () => Response.json(body);
}

describe('listFeedback response validation', () => {
  it('accepts the server cursor encoding and a valid own-list row', async () => {
    const response = await listFeedback(apiResponse({ items: [validItem], nextCursor: validCursor }));
    expect(response).toEqual({ items: [validItem], nextCursor: validCursor });
  });

  it.each([
    ['non-GUID ID', { ...validItem, id: 'not-a-guid' }],
    ['malformed creation date', { ...validItem, createdAt: 'yesterday' }],
    ['expiry before creation', { ...validItem, expiresAt: '2026-10-08T08:00:00Z' }],
  ])('rejects a row with %s', async (_description, item) => {
    await expect(listFeedback(apiResponse({ items: [item], nextCursor: null })))
      .rejects.toMatchObject({ code: 'invalid_response' });
  });

  it('rejects a cursor outside the server base64url timestamp-and-GUID contract', async () => {
    await expect(listFeedback(apiResponse({ items: [], nextCursor: 'next-page' })))
      .rejects.toMatchObject({ code: 'invalid_response' });
  });

  it('rejects own-list pages larger than the server page size', async () => {
    await expect(listFeedback(apiResponse({ items: Array.from({ length: 21 }, () => validItem), nextCursor: null })))
      .rejects.toMatchObject({ code: 'invalid_response' });
  });

  it('keeps malformed success responses distinguishable from transport failures', async () => {
    try {
      await listFeedback(apiResponse({ items: [{ ...validItem, id: 'bad' }], nextCursor: null }));
      throw new Error('expected invalid response');
    } catch (error) {
      expect(error).toBeInstanceOf(FeedbackApiError);
      expect(error).toMatchObject({ status: 200, code: 'invalid_response' });
    }
  });
});
