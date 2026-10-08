import React, { useId, useState, type ReactNode } from 'react';
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
  const [tooltipDismissed, setTooltipDismissed] = useState(false);
  const description = sourceOfTruth
    ? `${reason} ${messages.disabledReasonSourceLabel} ${sourceOfTruth}`
    : reason;

  return (
    <span
      className={`disabled-reason${tooltipDismissed ? ' disabled-reason--tooltip-dismissed' : ''}`}
      tabIndex={0}
      aria-describedby={tooltipId}
      onKeyDown={(event) => {
        if (event.key === 'Escape') setTooltipDismissed(true);
      }}
      onFocus={() => setTooltipDismissed(false)}
      onMouseEnter={() => setTooltipDismissed(false)}
    >
      {children}
      <span className="disabled-reason__text">
        {reason}
        {sourceOfTruth && <span className="disabled-reason__source"> {messages.disabledReasonSourceLabel} {sourceOfTruth}</span>}
      </span>
      <span className="disabled-reason__tooltip" id={tooltipId} role="tooltip" hidden={tooltipDismissed}>{description}</span>
    </span>
  );
}
