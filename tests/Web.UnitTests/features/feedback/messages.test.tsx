import { describe, expect, it } from 'vitest';
import { messages } from '../../../../src/Web/src/messages/en';
import { feedbackMessages } from '../../../../src/Web/src/features/feedback/messages';

type MissingFeedbackMessageKeys = Exclude<keyof typeof feedbackMessages, keyof typeof messages>;
const feedbackMessagesAreComposed: MissingFeedbackMessageKeys extends never ? true : never = true;

describe('feedback message catalog', () => {
  it('composes every typed feedback key into the English catalog', () => {
    expect(feedbackMessagesAreComposed).toBe(true);
    for (const key of Object.keys(feedbackMessages)) {
      expect(Object.prototype.hasOwnProperty.call(messages, key)).toBe(true);
    }
  });
});
