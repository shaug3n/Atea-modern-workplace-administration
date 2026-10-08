import React, { useId, type ReactNode } from 'react';

export function ExplainerPanel({
  title,
  children,
  id,
}: {
  title: string;
  children: ReactNode;
  id?: string;
}) {
  const titleId = useId();

  return (
    <section className="explainer-panel" aria-labelledby={titleId} id={id}>
      <h2 className="explainer-panel__title" id={titleId}>{title}</h2>
      <div className="explainer-panel__content">{children}</div>
    </section>
  );
}
