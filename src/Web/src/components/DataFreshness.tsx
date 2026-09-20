import React from 'react';
import { messages } from '../app/messages';

export function DataFreshness({ fetchedAt, freshness, partialData, message }: { fetchedAt?: string | null; freshness: 'fresh' | 'stale' | 'unavailable'; partialData: boolean; message?: string | null }) {
  const tone = freshness === 'fresh' && !partialData ? 'success' : freshness === 'stale' ? 'warning' : 'danger';
  const label = freshness === 'fresh' && !partialData
    ? messages.usersFresh
    : freshness === 'stale'
      ? messages.usersStale
      : messages.usersDataUnavailable;
  const fetched = fetchedAt ? new Date(fetchedAt).toLocaleString() : messages.usersNeverFetched;

  return (
    <div className="data-freshness" data-tone={tone} role={tone === 'success' ? 'status' : 'alert'}>
      <span>{label}</span>
      <span>{messages.usersFetchedAt}: {fetched}</span>
      {message && <span>{message}</span>}
    </div>
  );
}
