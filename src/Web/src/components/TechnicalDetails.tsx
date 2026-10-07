import React from 'react';
import { CopyValue } from './CopyValue';
import { Icon } from './icons';

export type TechnicalDetailItem = { label: string; value?: string | null; copy?: boolean };

export function TechnicalDetails({ summary = 'Technical details', items, className }: { summary?: string; items: TechnicalDetailItem[]; className?: string }) {
  const present = items.filter((item) => item.value);
  if (present.length === 0) return null;
  return (
    <details className={`technical-details${className ? ` ${className}` : ''}`}>
      <summary><Icon name="chevron" size={14} /> {summary}</summary>
      <dl>
        {present.map((item) => (
          <div key={item.label}>
            <dt>{item.label}</dt>
            <dd>{item.copy ? <CopyValue value={item.value!} label={item.label} /> : <code>{item.value}</code>}</dd>
          </div>
        ))}
      </dl>
    </details>
  );
}
