import React, { useId, type ReactNode } from 'react';
import { messages } from '../app/messages';

export function DisabledReason({
  children,
  reason,
  sourceOfTruth,
}: {
  children: ReactNode;
  reason: string;
  sourceOfTruth?: string;
}) {
  const tooltipId = useId();
  const description = sourceOfTruth
    ? `${reason} ${messages.disabledReasonSourceLabel} ${sourceOfTruth}`
    : reason;

  return (
    <span className="disabled-reason" tabIndex={0} aria-describedby={tooltipId}>
      {children}
      <span className="disabled-reason__text">
        {reason}
        {sourceOfTruth && <span className="disabled-reason__source"> {messages.disabledReasonSourceLabel} {sourceOfTruth}</span>}
      </span>
      <span className="disabled-reason__tooltip" id={tooltipId} role="tooltip">{description}</span>
    </span>
  );
}
