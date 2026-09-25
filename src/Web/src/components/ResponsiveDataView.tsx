import React, { useEffect, useState, type ReactNode } from 'react';

export type ResponsiveDataViewProps<T> = {
  items: T[];
  keyOf: (item: T) => string;
  label: string;
  renderTable: (items: T[]) => ReactNode;
  renderCompact: (item: T) => ReactNode;
};

const compactQuery = '(max-width: 64rem)';

export function ResponsiveDataView<T>({ items, keyOf, label, renderTable, renderCompact }: ResponsiveDataViewProps<T>) {
  const [compact, setCompact] = useState(() => typeof window.matchMedia === 'function' && window.matchMedia(compactQuery).matches);
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const query = window.matchMedia(compactQuery);
    const update = () => setCompact(query.matches);
    update();
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);

  return compact
    ? <ul className="responsive-data-view__compact" aria-label={label}>{items.map(item => <li key={keyOf(item)} className="responsive-data-view__item">{renderCompact(item)}</li>)}</ul>
    : <div className="responsive-data-view__table" role="region" aria-label={label} tabIndex={0}>{renderTable(items)}</div>;
}
