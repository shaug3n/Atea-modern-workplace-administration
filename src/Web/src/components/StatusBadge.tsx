import React from 'react';

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

export function StatusBadge({ tone = 'info', label, detail, density = 'default' }: { tone?: StatusTone; label: string; detail?: string; density?: 'default' | 'compact' }) {
  return (
    <span className={`status-badge${density === 'compact' ? ' status-badge--compact' : ''}`} data-tone={tone}>
      <span className="status-badge__dot" aria-hidden="true" />
      <span>{label}</span>
      {detail && <span className="status-badge__detail">{detail}</span>}
    </span>
  );
}
