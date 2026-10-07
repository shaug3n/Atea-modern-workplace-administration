import React from 'react';

export type MetricCardProps = {
  label: string;
  value?: string | number | null;
  detail?: string;
  href?: string;
  onNavigate?: (path: string) => void;
  linkLabel?: string;
  unavailableReason?: string;
  action?: { label: string; href: string };
};

export function MetricCard({ label, value, detail, href, onNavigate, linkLabel, unavailableReason, action }: MetricCardProps) {
  const hasValue = value !== null && value !== undefined && value !== '';
  const body = (
    <>
      <span className="metric-card__label">{label}</span>
      {hasValue
        ? <strong className="metric-card__value">{value}</strong>
        : <span className="metric-card__empty" aria-hidden={href ? 'true' : undefined}>—</span>}
      {detail && <span className="metric-card__detail">{detail}</span>}
      {!hasValue && unavailableReason && <span className="metric-card__detail">{unavailableReason}</span>}
    </>
  );
  const name = href ? `${label}${hasValue ? `: ${value}` : ''}, open ${linkLabel ?? label}` : undefined;
  return (
    <div className="metric-card">
      {href
        ? <a className="metric-card__link" href={href} aria-label={name} onClick={(event) => { if (onNavigate) { event.preventDefault(); onNavigate(href); } }}>{body}<span className="metric-card__arrow" aria-hidden="true">→</span></a>
        : <div className="metric-card__content">{body}</div>}
      {!hasValue && action && <a className="metric-card__action" href={action.href} onClick={(event) => { if (onNavigate) { event.preventDefault(); onNavigate(action.href); } }}>{action.label}</a>}
    </div>
  );
}
