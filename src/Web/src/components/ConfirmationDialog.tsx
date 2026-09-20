import React, { useEffect, useState } from 'react';
import { messages } from '../app/messages';

export type ConfirmationDialogProps = {
  title: string;
  target: string;
  proposedChange: string;
  requiredCapability: string;
  sourceLimitation?: string | null;
  auditNotice?: string;
  destructivePhrase?: string | null;
  busy?: boolean;
  onConfirm: () => void;
  onCancel?: () => void;
};

export function ConfirmationDialog({
  title,
  target,
  proposedChange,
  requiredCapability,
  sourceLimitation,
  auditNotice = messages.userMutationAuditNotice,
  destructivePhrase,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmationDialogProps) {
  const [reviewed, setReviewed] = useState(false);
  const [phrase, setPhrase] = useState('');
  const phraseMatches = !destructivePhrase || phrase === destructivePhrase;
  const canConfirm = reviewed && phraseMatches && !busy && !sourceLimitation;

  useEffect(() => {
    setReviewed(false);
    setPhrase('');
  }, [target]);

  return (
    <section className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="mutation-dialog-title">
      <h2 id="mutation-dialog-title">{title}</h2>
      <dl className="detail-list">
        <div>
          <dt>{messages.userMutationTarget}</dt>
          <dd>{target}</dd>
        </div>
        <div>
          <dt>{messages.userMutationProposedChange}</dt>
          <dd>{proposedChange}</dd>
        </div>
        <div>
          <dt>{messages.userRequiredCapability}</dt>
          <dd>{requiredCapability}</dd>
        </div>
      </dl>
      {sourceLimitation && <p role="alert">{sourceLimitation}</p>}
      <p>{auditNotice}</p>
      {destructivePhrase && (
        <label>
          {messages.userMutationDestructivePhraseLabel}
          <input value={phrase} onChange={(event) => setPhrase(event.target.value)} aria-label={messages.userMutationDestructivePhraseLabel} />
        </label>
      )}
      <label>
        <input type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} />
        {messages.userMutationReviewedConfirmation}
      </label>
      <div className="users-page__actions">
        {onCancel && <button type="button" onClick={onCancel}>{messages.userMutationCancel}</button>}
        <button type="button" disabled={!canConfirm} onClick={onConfirm}>{busy ? messages.userMutationSaving : messages.userMutationConfirm}</button>
      </div>
    </section>
  );
}
