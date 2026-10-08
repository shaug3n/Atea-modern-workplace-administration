import React from 'react';
import { messages } from '../app/messages';

export function LoadingSkeleton({ label = messages.statusLoading, lines = 3 }: { label?: string; lines?: number }) {
  const lineCount = Math.max(0, Math.floor(lines));

  return (
    <div className="loading-skeleton" role="status">
      <span className="sr-only">{label}</span>
      <div className="loading-skeleton__placeholders" aria-hidden="true">
        {Array.from({ length: lineCount }, (_, index) => (
          <span key={index} className="loading-skeleton__line" />
        ))}
      </div>
    </div>
  );
}
