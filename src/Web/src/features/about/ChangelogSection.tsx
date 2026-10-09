import React, { useMemo, useState } from 'react';
import { messages } from '../../app/messages';
import type { ChangelogEntry, ChangelogType } from './content';

type ChangelogFilter = 'All' | ChangelogType;

const filters: Array<{ value: ChangelogFilter; label: string }> = [
  { value: 'All', label: messages.aboutChangelogAll },
  { value: 'New', label: messages.aboutChangelogNew },
  { value: 'Improved', label: messages.aboutChangelogImproved },
  { value: 'Fixed', label: messages.aboutChangelogFixed },
];

export function ChangelogSection({ entries }: { entries: ChangelogEntry[] }) {
  const [filter, setFilter] = useState<ChangelogFilter>('All');
  const visibleEntries = useMemo(
    () => filter === 'All' ? entries : entries.filter(entry => entry.type === filter),
    [entries, filter],
  );

  return (
    <section aria-labelledby="about-changelog-heading">
      <h2 id="about-changelog-heading">{messages.aboutChangelogHeading}</h2>
      <div className="about-filter-list" role="group" aria-label={messages.aboutChangelogHeading}>
        {filters.map(item => (
          <button
            aria-pressed={filter === item.value}
            className={filter === item.value ? 'about-filter is-active' : 'about-filter'}
            key={item.value}
            onClick={() => setFilter(item.value)}
            type="button"
          >
            {item.label}
          </button>
        ))}
      </div>
      {visibleEntries.length === 0 ? (
        <p className="about-empty-state" role="status">{messages.aboutChangelogEmpty}</p>
      ) : (
        <ol className="about-changelog-list">
          {visibleEntries.map(entry => (
            <li className="about-changelog-entry" data-changelog-entry={entry.id} key={entry.id}>
              <div className="about-changelog-entry__meta">
                <time>{entry.release}</time>
                <span className={`about-entry-type about-entry-type--${entry.type.toLowerCase()}`}>{entry.type}</span>
              </div>
              <p>{entry.description}</p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
