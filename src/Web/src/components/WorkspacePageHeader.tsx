import React, { type ReactNode } from 'react';

export type WorkspacePageHeaderProps = {
  title: string;
  description?: string;
  eyebrow?: string;
  actions?: ReactNode;
  children?: ReactNode;
};

export function WorkspacePageHeader({ title, description, eyebrow, actions, children }: WorkspacePageHeaderProps) {
  return <header className="workspace-page-header">
    <div className="workspace-page-header__intro">
      {eyebrow && <p className="eyebrow">{eyebrow}</p>}
      <h1>{title}</h1>
      {description && <p className="workspace-page-header__description">{description}</p>}
      {children}
    </div>
    {actions && <div className="workspace-page-header__actions">{actions}</div>}
  </header>;
}
