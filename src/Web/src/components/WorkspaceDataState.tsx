import React from 'react';
import { Icon, type IconName } from './icons';

export type DataStateKind = 'loading' | 'empty' | 'unavailable' | 'partial' | 'permission' | 'stale';

export type DataStateProps = {
  kind?: DataStateKind;
  state?: 'loading' | 'empty' | 'unavailable';
  title?: string;
  message: string;
  affected?: string[];
  action?: { label: string; href?: string; onClick?: () => void };
  onRetry?: () => void;
  retryLabel?: string;
  retrying?: boolean;
  compact?: boolean;
  technical?: { label: string; value: string }[];
};

const icons: Partial<Record<DataStateKind, IconName>> = {
  unavailable: 'alert-circle',
  partial: 'alert-triangle',
  permission: 'lock',
  stale: 'clock'
};

export function WorkspaceDataState({ kind, state, title, message, affected, action, onRetry, retryLabel, retrying, compact, technical }: DataStateProps) {
  const resolved: DataStateKind = kind ?? state ?? 'empty';
  const icon = icons[resolved];
  const showRetry = onRetry && (resolved === 'unavailable' || resolved === 'partial' || resolved === 'stale');
  const className = `async-state workspace-data-state workspace-data-state--${resolved}${compact ? ' workspace-data-state--compact' : ''}`;
  return (
    <section className={className} role={resolved === 'unavailable' ? 'alert' : 'status'} aria-busy={resolved === 'loading' ? 'true' : undefined}>
      {icon && <span className="workspace-data-state__icon"><Icon name={icon} size={20} /></span>}
      {resolved === 'loading' && <span className="workspace-data-state__spinner" aria-hidden="true" />}
      <div className="workspace-data-state__body">
        {title && <strong className="workspace-data-state__title">{title}</strong>}
        <p>{message}</p>
        {affected && affected.length > 0 && <ul className="workspace-data-state__affected">{affected.map((item) => <li key={item}>{item}</li>)}</ul>}
        {(showRetry || action) && (
          <div className="workspace-data-state__actions">
            {showRetry && <button type="button" className="button button--secondary" onClick={onRetry} disabled={retrying}>{retrying ? 'Retrying…' : retryLabel ?? 'Retry'}</button>}
            {action && (action.href
              ? <a className="button button--tertiary" href={action.href} onClick={action.onClick}>{action.label}</a>
              : <button type="button" className="button button--tertiary" onClick={action.onClick}>{action.label}</button>)}
          </div>
        )}
      </div>
    </section>
  );
}
