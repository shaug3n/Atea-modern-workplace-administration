import React, { type ReactNode } from 'react';

export function ProvenanceChip({ source, explanation }: { source: string; explanation?: ReactNode }) {
  return (
    <span className="provenance-chip">
      <span className="provenance-chip__source">{source}</span>
      {explanation !== undefined && <span className="provenance-chip__explanation">{explanation}</span>}
    </span>
  );
}
