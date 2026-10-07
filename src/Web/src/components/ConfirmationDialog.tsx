import React, { useEffect, useId, useState } from 'react';
import { messages } from '../app/messages';
import { humanizeCapability } from '../format/humanize';
import { Icon } from './icons';
import { TechnicalDetails } from './TechnicalDetails';
import { useFocusContainment } from './useFocusContainment';

export type ConfirmationDialogProps = {
  title: string;
  target: string;
  proposedChange: string;
  requiredCapability: string;
  sourceLimitation?: string | null;
  auditNotice?: string;
  destructivePhrase?: string | null;
  destructivePhraseLabel?: string;
  busy?: boolean;
  onConfirm: () => void;
  onCancel?: () => void;
  children?: React.ReactNode;
  embedded?: boolean;
  showTitle?: boolean;
  confirmLabel: string;
  consequence?: string;
  tone?: 'default' | 'danger';
};

export function ConfirmationDialog({
  title,
  target,
  proposedChange,
  requiredCapability,
  sourceLimitation,
  auditNotice = messages.userMutationAuditNotice,
  destructivePhrase,
  destructivePhraseLabel = messages.userMutationDestructivePhraseLabel,
  busy = false,
  onConfirm,
  onCancel,
  children,
  embedded = false,
  showTitle = true,
  confirmLabel,
  consequence,
  tone = 'default',
}: ConfirmationDialogProps) {
  const [reviewed, setReviewed] = useState(false);
  const [phrase, setPhrase] = useState('');
  const consequenceId = useId();
  const phraseMatches = !destructivePhrase || phrase === destructivePhrase;
  const canConfirm = reviewed && phraseMatches && !busy && !sourceLimitation;
  const dialogRef = useFocusContainment<HTMLElement>(!embedded, onCancel && !busy ? onCancel : undefined);

  useEffect(() => {
    setReviewed(false);
    setPhrase('');
  }, [target]);

  useEffect(() => {
    if (!onCancel || busy) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [busy, onCancel]);

  const danger = tone === 'danger';
  const content = (
    <>
      {showTitle && <h2 id="mutation-dialog-title">{title}</h2>}
      {consequence && (
        <p id={consequenceId} className={`mutation-consequence${danger ? ' mutation-consequence--danger' : ''}`}>
          {danger && <Icon name="alert-triangle" size={18} />}
          <span>{consequence}</span>
        </p>
      )}
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
          <dd>{humanizeCapability(requiredCapability)}</dd>
        </div>
      </dl>
      <TechnicalDetails items={[{ label: 'Capability key', value: requiredCapability }]} />
      {children}
      {sourceLimitation && <p role="alert">{sourceLimitation}</p>}
      <p className="mutation-audit-notice">{auditNotice}</p>
      {destructivePhrase && (
        <label className="mutation-phrase">
          <span>{destructivePhraseLabel}</span>
          <input value={phrase} onChange={(event) => setPhrase(event.target.value)} autoComplete="off" />
        </label>
      )}
      <label className="checkbox-field">
        <input type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} />
        <span>{messages.userMutationReviewedConfirmation}</span>
      </label>
      <div className="page-action-bar mutation-dialog__footer">
        {onCancel && <button type="button" className="button button--secondary" onClick={onCancel}>{messages.userMutationCancel}</button>}
        <button type="button" className={danger ? 'button button--danger button--danger-solid' : 'button button--primary'} disabled={!canConfirm} onClick={onConfirm}>{busy ? `${confirmLabel}…` : confirmLabel}</button>
      </div>
    </>
  );

  if (embedded) return <div className="mutation-confirmation">{content}</div>;
  return <div className="modal-backdrop"><section ref={dialogRef} className="mutation-dialog" role="dialog" aria-modal="true" aria-labelledby="mutation-dialog-title" aria-describedby={consequence ? consequenceId : undefined}>{content}</section></div>;
}
