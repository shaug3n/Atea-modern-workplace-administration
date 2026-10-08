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
  valueTitle?: string;
};

export type MetricCardBodyProps = Pick<MetricCardProps, 'label' | 'value' | 'detail' | 'unavailableReason' | 'valueTitle'>;

export function MetricCardBody({ label, value, detail, unavailableReason, valueTitle }: MetricCardBodyProps) {
  const hasValue = value !== null && value !== undefined && value !== '';
  return (
    <>
      <span className="metric-card__label">{label}</span>
      {hasValue
        ? <strong className="metric-card__value" title={valueTitle}>{value}</strong>
        : <span className="metric-card__empty">—</span>}
      {detail && <span className="metric-card__detail">{detail}</span>}
      {!hasValue && unavailableReason && <span className="metric-card__detail">{unavailableReason}</span>}
    </>
  );
}

export function MetricCard({ label, value, detail, href, onNavigate, linkLabel, unavailableReason, action, valueTitle }: MetricCardProps) {
  const hasValue = value !== null && value !== undefined && value !== '';
  const name = href ? `${label}${hasValue ? `: ${value}` : ''}, open ${linkLabel ?? label}` : undefined;
  return (
    <div className="metric-card">
      {href
        ? <a className="metric-card__link" href={href} aria-label={name} onClick={(event) => { if (onNavigate) { event.preventDefault(); onNavigate(href); } }}><MetricCardBody label={label} value={value} detail={detail} unavailableReason={unavailableReason} valueTitle={valueTitle} /><span className="metric-card__arrow" aria-hidden="true">→</span></a>
        : <div className="metric-card__content"><MetricCardBody label={label} value={value} detail={detail} unavailableReason={unavailableReason} valueTitle={valueTitle} /></div>}
      {!hasValue && action && <a className="metric-card__action" href={action.href} onClick={(event) => { if (onNavigate) { event.preventDefault(); onNavigate(action.href); } }}>{action.label}</a>}
    </div>
  );
}
