import React from 'react';

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

export function StatusBadge({ tone = 'info', label, detail }: { tone?: StatusTone; label: string; detail?: string }) {
  return (
    <span className="status-badge" data-tone={tone}>
      <span>{label}</span>
      {detail && <span className="status-badge__detail">{detail}</span>}
    </span>
  );
}
