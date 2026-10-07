import React, { type ReactNode } from 'react';

export type WorkspacePageHeaderProps = {
  title: string;
  description?: string;
  eyebrow?: string;
  actions?: ReactNode;
  children?: ReactNode;
  backLink?: { label: string; href: string; onNavigate?: (path: string) => void };
  meta?: ReactNode;
};

export function WorkspacePageHeader({ title, description, eyebrow, actions, children, backLink, meta }: WorkspacePageHeaderProps) {
  return <header className="workspace-page-header">
    <div className="workspace-page-header__intro">
      {backLink && <a className="workspace-page-header__back" href={backLink.href} onClick={(event) => { if (backLink.onNavigate) { event.preventDefault(); backLink.onNavigate(backLink.href); } }}>← {backLink.label}</a>}
      {eyebrow && <p className="eyebrow">{eyebrow}</p>}
      <h1 id="page-title">{title}</h1>
      {description && <p className="workspace-page-header__description">{description}</p>}
      {meta && <div className="workspace-page-header__meta">{meta}</div>}
      {children}
    </div>
    {actions && <div className="workspace-page-header__actions">{actions}</div>}
  </header>;
}
