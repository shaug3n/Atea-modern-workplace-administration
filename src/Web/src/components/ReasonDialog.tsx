import React, { useId, useState } from 'react';
import { messages } from '../app/messages';
import { ConfirmationDialog, type ConfirmationDialogProps } from './ConfirmationDialog';

export type ReasonDialogProps = Omit<ConfirmationDialogProps, 'onConfirm' | 'children' | 'confirmBlocked'> & {
  onConfirm: (reason: string) => void;
  reasonHint?: string;
};

export function ReasonDialog({ onConfirm, reasonHint, ...props }: ReasonDialogProps) {
  const [reason, setReason] = useState('');
  const reasonId = useId();
  const requiredId = useId();
  const hintId = useId();
  const trimmedReason = reason.trim();

  return (
    <ConfirmationDialog
      {...props}
      confirmBlocked={!trimmedReason}
      onConfirm={() => onConfirm(trimmedReason)}
    >
      <div className="mutation-phrase">
        <label htmlFor={reasonId}>{messages.reasonFieldLabel}</label>
        <textarea
          id={reasonId}
          value={reason}
          required
          aria-describedby={`${requiredId}${reasonHint ? ` ${hintId}` : ''}`}
          onChange={(event) => setReason(event.target.value)}
        />
        <span id={requiredId} className="mutation-reason-required">{messages.reasonFieldRequired}</span>
        {reasonHint && <span id={hintId} className="mutation-reason-hint">{reasonHint}</span>}
      </div>
    </ConfirmationDialog>
  );
}
