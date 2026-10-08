import React from 'react';
import { DateTime } from './DateTime';
import { Icon } from './icons';
import { StatusBadge } from './StatusBadge';
import { messages } from '../app/messages';

export type DataFreshnessLabels = {
  fresh: string;
  stale: string;
  unavailable: string;
  partial: string;
  fetched: string;
  throttled: string;
};

const defaultLabels: DataFreshnessLabels = {
  fresh: messages.statusUpToDate,
  stale: messages.statusStale,
  unavailable: messages.statusUnavailable,
  partial: messages.statusPartial,
  fetched: messages.statusUpdated,
  throttled: messages.statusThrottled
};

type Props = {
  fetchedAt?: string | null;
  freshness: 'fresh' | 'stale' | 'unavailable';
  partialData: boolean;
  message?: string | null;
  labels?: Partial<DataFreshnessLabels>;
  presentation?: 'line' | 'pill' | 'banner';
  throttled?: boolean;
  source?: string;
  onRefresh?: () => void;
  refreshing?: boolean;
};

export function DataFreshness({ fetchedAt, freshness, partialData, message, labels, presentation = 'line', throttled = false, source, onRefresh, refreshing }: Props) {
  const copy = { ...defaultLabels, ...labels };
  const ok = freshness === 'fresh' && !partialData;
  const tone = ok ? 'neutral' : freshness === 'stale' ? 'warning' : freshness === 'unavailable' ? 'danger' : 'warning';
  const label = ok ? copy.fresh : freshness === 'stale' ? copy.stale : freshness === 'unavailable' ? copy.unavailable : copy.partial;
  const icon = ok ? null : freshness === 'stale' ? 'clock' : freshness === 'unavailable' ? 'alert-circle' : 'alert-triangle';

  if (presentation !== 'line') {
    const freshnessTone = freshness === 'fresh' ? 'neutral' : freshness === 'stale' ? 'warning' : 'danger';
    return (
      <div className={`data-freshness data-freshness--${presentation}`} role="status">
        <div className="data-freshness__states">
          <StatusBadge tone={freshnessTone} label={copy[freshness]} density="compact" />
          {partialData && <StatusBadge tone="warning" label={copy.partial} density="compact" />}
          {throttled && <StatusBadge tone="warning" label={copy.throttled} density="compact" />}
        </div>
        <div className="data-freshness__details">
          <span>{copy.fetched} <DateTime value={fetchedAt} relative /></span>
          {source && <span>{source}</span>}
          {message && <span className="data-freshness__message">{message}</span>}
          {onRefresh && <button type="button" className="button button--tertiary button--sm" onClick={onRefresh} disabled={refreshing}>{refreshing ? messages.statusRefreshing : messages.statusRefreshAction}</button>}
        </div>
      </div>
    );
  }

  return (
    <div className="data-freshness" data-tone={tone} role="status">
      {icon && <Icon name={icon} size={14} />}
      <span className="data-freshness__label">{label}</span>
      <span className="data-freshness__sep" aria-hidden="true">·</span>
      <span>{copy.fetched} <DateTime value={fetchedAt} relative /></span>
      {source && <><span className="data-freshness__sep" aria-hidden="true">·</span><span>{source}</span></>}
      {message && !ok && <span className="data-freshness__message">{message}</span>}
      {throttled && <StatusBadge tone="warning" label={copy.throttled} density="compact" />}
      {onRefresh && <button type="button" className="button button--tertiary button--sm" onClick={onRefresh} disabled={refreshing}>{refreshing ? messages.statusRefreshing : messages.statusRefreshAction}</button>}
    </div>
  );
}
