import React, { type ReactNode } from 'react';

export function ActionGroup({ title, description, tone = 'default', children }: { title?: string; description?: string; tone?: 'default' | 'danger'; children: ReactNode }) {
  const heading = title ?? (tone === 'danger' ? 'Danger zone' : undefined);
  return (
    <section className={`action-group${tone === 'danger' ? ' action-group--danger' : ''}`}>
      {heading && <h3>{heading}</h3>}
      {description && <p className="action-group__description">{description}</p>}
      <div className="action-group__body">{children}</div>
    </section>
  );
}
