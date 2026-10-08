import React from 'react';
import type { CapabilityDecision } from '../capabilities/capabilityTypes';
import { messages } from '../app/messages';
import { PermissionState } from './PermissionState';
import { StatusBadge } from './StatusBadge';

export function DomainAccessChip({
  label,
  viewDecision,
  viewReason,
  writeDecision,
  writeReason,
}: {
  label: string;
  viewDecision: CapabilityDecision;
  viewReason: string;
  writeDecision?: CapabilityDecision;
  writeReason?: string;
}) {
  if (viewDecision.state === 'hidden') {
    return null;
  }

  const writeAllowed = writeDecision?.state === 'allowed';
  const viewAllowed = viewDecision.state === 'allowed';
  const accessLabel = writeAllowed
    ? messages.domainAccessReadWrite
    : viewAllowed
      ? messages.domainAccessReadOnly
      : messages.domainAccessUnavailable;

  return (
    <PermissionState decision={viewDecision} reasonText={viewReason}>
      <span className="domain-access-chip">
        <span className="domain-access-chip__label">{label}</span>
        <StatusBadge
          tone={writeAllowed ? 'success' : viewAllowed ? 'info' : 'neutral'}
          label={accessLabel}
          density="compact"
        />
        {viewAllowed && <span className="domain-access-chip__reason">{viewReason}</span>}
        {writeReason && <span className="domain-access-chip__reason">{writeReason}</span>}
      </span>
    </PermissionState>
  );
}
