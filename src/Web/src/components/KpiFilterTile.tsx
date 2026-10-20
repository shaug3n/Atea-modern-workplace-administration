import React from 'react';
import { Icon } from './icons';
import { MetricCardBody } from './MetricCard';

type KpiFilterTileBaseProps = {
  label: string;
  value?: string | number | null;
  detail?: string;
  valueTitle?: string;
};

export type KpiFilterTileProps = KpiFilterTileBaseProps & (
  | { selected: boolean; onClick: () => void; href?: never; linkLabel?: never }
  | { href: string; linkLabel: string; onClick?: React.MouseEventHandler<HTMLAnchorElement>; selected?: never }
);

export function KpiFilterTile(props: KpiFilterTileProps) {
  const { label, value, detail, valueTitle } = props;
  if (typeof props.href === 'string') {
    return (
      <a className="metric-card metric-card--filter metric-card__link" href={props.href} aria-label={props.linkLabel} onClick={props.onClick}>
        <span className="metric-card__content">
          <MetricCardBody label={label} value={value} detail={detail} valueTitle={valueTitle} />
        </span>
      </a>
    );
  }

  return (
    <button className="metric-card metric-card--filter" type="button" aria-pressed={props.selected} onClick={props.onClick}>
      <span className="metric-card__content">
        <MetricCardBody label={label} value={value} detail={detail} valueTitle={valueTitle} />
      </span>
      {props.selected && <Icon name="check" size={16} />}
    </button>
  );
}
