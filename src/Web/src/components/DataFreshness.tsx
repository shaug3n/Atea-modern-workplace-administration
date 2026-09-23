import React from 'react';
import { messages } from '../app/messages';

export type DataFreshnessLabels = {
  fresh: string;
  stale: string;
  unavailable: string;
  fetched: string;
};

const defaultLabels: DataFreshnessLabels = {
  fresh: messages.usersFresh,
  stale: messages.usersStale,
  unavailable: messages.usersDataUnavailable,
  fetched: messages.usersFetchedAt,
};

export function DataFreshness({ fetchedAt, freshness, partialData, message, labels }: { fetchedAt?: string | null; freshness: 'fresh' | 'stale' | 'unavailable'; partialData: boolean; message?: string | null; labels?: Partial<DataFreshnessLabels> }) {
  const tone = freshness === 'fresh' && !partialData ? 'success' : freshness === 'stale' ? 'warning' : 'danger';
  const copy = { ...defaultLabels, ...labels };
  const label = freshness === 'fresh' && !partialData
    ? copy.fresh
    : freshness === 'stale'
      ? copy.stale
      : copy.unavailable;
  const fetched = fetchedAt ? new Date(fetchedAt).toLocaleString() : messages.usersNeverFetched;

  return (
    <div className="data-freshness" data-tone={tone} role={tone === 'success' ? 'status' : 'alert'}>
      <span>{label}</span>
      <span>{copy.fetched}: {fetched}</span>
      {message && <span>{message}</span>}
    </div>
  );
}
