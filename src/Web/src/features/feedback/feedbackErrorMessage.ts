import { messages } from '../../app/messages';
import { FeedbackApiError } from './feedbackApi';

type FeedbackOperation = 'load' | 'save';

export function feedbackErrorMessage(error: unknown, operation: FeedbackOperation): string {
  if (!(error instanceof FeedbackApiError)) {
    return operation === 'load' ? messages.feedbackLoadUnavailable : messages.feedbackSaveUnavailable;
  }
  if (error.status === 401) return messages.feedbackSessionExpired;
  if (error.status === 403) return messages.feedbackAccessDenied;
  if (error.code === 'validation_failed' || error.status === 400 || error.status === 422) return messages.feedbackValidationFailed;
  if (error.code === 'idempotency_key_expired') return messages.feedbackRetryExpired;
  if (error.code === 'idempotency_key_reused') return messages.feedbackRetryConflict;
  if (error.status === 409) return messages.feedbackConflict;
  if (error.code === 'network_error' || error.status === undefined) {
    return operation === 'load' ? messages.feedbackLoadNetworkError : messages.feedbackSaveNetworkError;
  }
  if (error.status >= 500) {
    return operation === 'load' ? messages.feedbackLoadServerError : messages.feedbackSaveServerError;
  }
  return operation === 'load' ? messages.feedbackLoadUnavailable : messages.feedbackSaveUnavailable;
}
