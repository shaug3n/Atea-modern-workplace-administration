import React, { useEffect, useId, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { useFocusContainment } from '../../components/useFocusContainment';
import { messages } from '../../app/messages';
import { createFeedback, FeedbackApiError, type FeedbackCategory, type FeedbackRequest } from './feedbackApi';
import './feedback.css';

type FormValues = Omit<FeedbackRequest, 'category'> & { category: string };
type FormErrors = Partial<Record<keyof FormValues, string>>;

const blankForm: FormValues = { category: '', subject: '', message: '' };
const dotNetWhitespace = /^[\u0009-\u000D\u0020\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]*$/;

function isFeedbackCategory(value: string): value is FeedbackCategory {
  return value === 'Bug' || value === 'Improvement' || value === 'General';
}

function isBlank(value: string) {
  return dotNetWhitespace.test(value);
}

function validate(values: FormValues): FormErrors {
  const errors: FormErrors = {};
  if (!isFeedbackCategory(values.category)) errors.category = messages.feedbackCategoryRequired;
  if (isBlank(values.subject)) errors.subject = messages.feedbackSubjectRequired;
  else if (values.subject.length > 120) errors.subject = messages.feedbackSubjectLimit;
  if (isBlank(values.message)) errors.message = messages.feedbackMessageRequired;
  else if (values.message.length > 4_000) errors.message = messages.feedbackMessageLimit;
  return errors;
}

function newIdempotencyKey() {
  return globalThis.crypto.randomUUID();
}

export function FeedbackComposerDialog({ open, onOpenChange, workspaceId, submitterObjectId, onSaved }: { open: boolean; onOpenChange: (open: boolean) => void; workspaceId: string; submitterObjectId: string; onSaved: () => void }) {
  const api = useApi();
  const titleId = useId();
  const privacyId = useId();
  const [values, setValues] = useState<FormValues>(blankForm);
  const [errors, setErrors] = useState<FormErrors>({});
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState(false);
  const retry = useRef<{ payload: string; key: string } | null>(null);
  const contextKey = `${workspaceId}:${submitterObjectId}`;
  const contextRef = useRef(contextKey);
  const requestGeneration = useRef(0);
  const latestValues = useRef(values);
  const categoryRef = useRef<HTMLSelectElement>(null);
  const dialogRef = useFocusContainment<HTMLDivElement>(open, () => {
    if (!pending) onOpenChange(false);
  });

  useEffect(() => () => {
    requestGeneration.current += 1;
  }, []);

  useEffect(() => {
    if (open) categoryRef.current?.focus();
  }, [open]);

  useEffect(() => {
    if (!open || contextRef.current !== contextKey) {
      contextRef.current = contextKey;
      requestGeneration.current += 1;
      latestValues.current = blankForm;
      setValues(blankForm);
      setErrors({});
      setPending(false);
      setError('');
      setSaved(false);
      retry.current = null;
    }
  }, [open, contextKey]);

  if (!open) return null;

  const update = (field: keyof FormValues, value: string) => {
    const nextValues = { ...latestValues.current, [field]: value };
    latestValues.current = nextValues;
    setValues(nextValues);
    setErrors(current => ({ ...current, [field]: undefined }));
    setError('');
    setSaved(false);
    retry.current = null;
  };

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validation = validate(values);
    setErrors(validation);
    setError('');
    setSaved(false);
    if (Object.keys(validation).length > 0) return;
    if (!isFeedbackCategory(values.category)) return;

    const request = { category: values.category, subject: values.subject, message: values.message };
    const payload = JSON.stringify(request);
    const key = retry.current?.payload === payload ? retry.current.key : newIdempotencyKey();
    const requestId = ++requestGeneration.current;
    retry.current = { payload, key };
    setPending(true);
    try {
      await createFeedback(api, request, key);
      if (requestId !== requestGeneration.current) return;
      retry.current = null;
      if (JSON.stringify(latestValues.current) === payload) {
        latestValues.current = blankForm;
        setValues(blankForm);
        setErrors({});
      }
      setSaved(true);
      onSaved();
    } catch (reason) {
      if (requestId !== requestGeneration.current) return;
      if (reason instanceof FeedbackApiError && reason.code === 'idempotency_key_expired') {
        retry.current = { payload, key: newIdempotencyKey() };
        setError(messages.feedbackRetryExpired);
      } else if (reason instanceof FeedbackApiError && reason.code === 'idempotency_key_reused') {
        retry.current = null;
        setError(messages.feedbackRetryConflict);
      } else {
        setError(messages.feedbackSaveFailed);
      }
    } finally {
      if (requestId === requestGeneration.current) setPending(false);
    }
  };

  return (
    <div className="feedback-dialog-backdrop">
      <div
        aria-labelledby={titleId}
        aria-describedby={privacyId}
        aria-modal="true"
        className="feedback-dialog"
        ref={dialogRef}
        role="dialog"
      >
        <h2 id={titleId}>{messages.feedbackTitle}</h2>
        <form onSubmit={event => void submit(event)} noValidate>
          <label htmlFor={`${titleId}-category`}>{messages.feedbackCategoryLabel}</label>
          <select ref={categoryRef} id={`${titleId}-category`} value={values.category} aria-invalid={Boolean(errors.category)} aria-describedby={errors.category ? `${titleId}-category-error` : undefined} required onChange={event => update('category', event.target.value)}>
            <option value="">{messages.feedbackCategoryPlaceholder}</option>
            <option value="Bug">{messages.feedbackCategoryBug}</option>
            <option value="Improvement">{messages.feedbackCategoryImprovement}</option>
            <option value="General">{messages.feedbackCategoryGeneral}</option>
          </select>
          {errors.category && <p id={`${titleId}-category-error`} className="feedback-field-error">{errors.category}</p>}

          <label htmlFor={`${titleId}-subject`}>{messages.feedbackSubjectLabel}</label>
          <input id={`${titleId}-subject`} maxLength={120} value={values.subject} aria-invalid={Boolean(errors.subject)} aria-describedby={errors.subject ? `${titleId}-subject-error` : undefined} required onChange={event => update('subject', event.target.value)} />
          {errors.subject && <p id={`${titleId}-subject-error`} className="feedback-field-error">{errors.subject}</p>}

          <label htmlFor={`${titleId}-message`}>{messages.feedbackMessageLabel}</label>
          <textarea id={`${titleId}-message`} maxLength={4000} rows={5} value={values.message} aria-invalid={Boolean(errors.message)} aria-describedby={errors.message ? `${titleId}-message-error` : undefined} required onChange={event => update('message', event.target.value)} />
          {errors.message && <p id={`${titleId}-message-error`} className="feedback-field-error">{errors.message}</p>}

          <aside id={privacyId} className="feedback-privacy">
            <h3>{messages.feedbackPrivacyTitle}</h3>
            <p>{messages.feedbackPrivacySensitive}</p>
            <p>{messages.feedbackPrivacyDelivery}</p>
            <p>{messages.feedbackPrivacyRetention}</p>
          </aside>
          {error && <p className="feedback-form-error" role="alert">{error}</p>}
          {saved && <p className="feedback-saved" role="status">{messages.feedbackSaved}</p>}
          <div className="feedback-dialog__actions">
            <button className="button button--secondary" type="button" disabled={pending} onClick={() => onOpenChange(false)}>{messages.feedbackCancel}</button>
            <button className="button button--primary" type="submit" disabled={pending}>{pending ? messages.feedbackSaving : messages.feedbackSaveAction}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
