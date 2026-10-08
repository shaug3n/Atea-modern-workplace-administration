import React from 'react';
import { Icon } from './icons';
import { MetricCardBody } from './MetricCard';

export type KpiFilterTileProps = {
  label: string;
  value?: string | number | null;
  detail?: string;
  selected: boolean;
  onClick: () => void;
  valueTitle?: string;
};

export function KpiFilterTile({ label, value, detail, selected, onClick, valueTitle }: KpiFilterTileProps) {
  return (
    <button className="metric-card metric-card--filter" type="button" aria-pressed={selected} onClick={onClick}>
      <span className="metric-card__content">
        <MetricCardBody label={label} value={value} detail={detail} valueTitle={valueTitle} />
      </span>
      {selected && <Icon name="check" size={16} />}
    </button>
  );
}
