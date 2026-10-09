import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { AboutPage } from '../../../../src/Web/src/features/about/AboutPage';
import { ChangelogSection } from '../../../../src/Web/src/features/about/ChangelogSection';
import { changelogEntries } from '../../../../src/Web/src/features/about/content';

describe('About page', () => {
  afterEach(cleanup);

  it('exposes the five approved sections as accessible tabs', () => {
    render(<AboutPage />);

    const tabs = screen.getAllByRole('tab').map(tab => tab.textContent);
    expect(tabs).toEqual(['Overview', 'Security & compliance', 'Architecture', 'Privacy', 'Changelog']);
    expect(screen.getByRole('tab', { name: 'Overview' }).getAttribute('aria-selected')).toBe('true');
    expect(screen.getByRole('link', { name: 'System versions' }).getAttribute('href')).toBe('/about/system-versions');
  });

  it('moves between tabs with the arrow and Home/End keys', () => {
    render(<AboutPage />);
    const overviewTab = screen.getByRole('tab', { name: 'Overview' });
    overviewTab.focus();
    fireEvent.keyDown(overviewTab, { key: 'ArrowRight' });
    expect(screen.getByRole('tab', { name: 'Security & compliance' }).getAttribute('aria-selected')).toBe('true');
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Security & compliance' }), { key: 'End' });
    expect(screen.getByRole('tab', { name: 'Changelog' }).getAttribute('aria-selected')).toBe('true');
  });

  it('filters changelog entries without changing their original order', () => {
    render(<ChangelogSection entries={changelogEntries} />);

    const initialOrder = [...document.querySelectorAll<HTMLElement>('[data-changelog-entry]')]
      .map(entry => entry.dataset.changelogEntry);
    for (const label of ['All', 'New', 'Improved', 'Fixed']) {
      expect(screen.getByRole('button', { name: label })).toBeTruthy();
    }

    for (const type of ['New', 'Improved', 'Fixed'] as const) {
      fireEvent.click(screen.getByRole('button', { name: type }));
      const filteredEntries = [...document.querySelectorAll<HTMLElement>('[data-changelog-entry]')];
      expect(filteredEntries.length).toBeGreaterThan(0);
      expect(filteredEntries.every(entry => within(entry).getByText(type))).toBe(true);
      expect(filteredEntries.map(entry => entry.dataset.changelogEntry))
        .toEqual(initialOrder.filter(id => changelogEntries.find(item => item.id === id)?.type === type));
    }
    fireEvent.click(screen.getByRole('button', { name: 'All' }));
    expect([...document.querySelectorAll<HTMLElement>('[data-changelog-entry]')].map(entry => entry.dataset.changelogEntry))
      .toEqual(initialOrder);
  });

  it('shows a useful empty state when a changelog filter has no matches', () => {
    render(<ChangelogSection entries={[]} />);
    expect(screen.getByText('No changelog entries match this filter.')).toBeTruthy();
  });

  it('renders changelog descriptions as text rather than HTML', () => {
    render(<ChangelogSection entries={[{
      id: 'markup-description',
      release: 'Verified release · 2026-10-08',
      type: 'New',
      description: '<img src=x onerror=alert(1)>',
    }]} />);
    expect(screen.getByText('<img src=x onerror=alert(1)>')).toBeTruthy();
    expect(document.querySelector('img')).toBeNull();
  });

  it('renders security and privacy copy from verified repository facts without HTML injection or unsupported claims', () => {
    render(<AboutPage />);
    fireEvent.click(screen.getByRole('tab', { name: 'Security & compliance' }));
    expect(screen.getByText(/verified tenant and object claims/i)).toBeTruthy();
    expect(screen.getByText(/Graph access tokens stay server-side/i)).toBeTruthy();
    fireEvent.click(screen.getByRole('tab', { name: 'Privacy' }));
    expect(screen.getByText(/Feedback stores the user-entered subject and message as plain text/i)).toBeTruthy();
    expect(screen.getAllByText(/90 days/i).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText(/backup retention is separate/i)).toBeTruthy();
    expect(screen.queryByText(/certified|guaranteed|AI assistant/i)).toBeNull();
    expect(document.querySelector('script, img')).toBeNull();
  });

  it('qualifies metadata storage and discloses user-entered feedback text and its scope', () => {
    render(<AboutPage />);
    fireEvent.click(screen.getByRole('tab', { name: 'Architecture' }));
    expect(screen.getByText(/workspace-administration and Microsoft Graph integration storage/i)).toBeTruthy();
    expect(screen.queryByText(/does not store user-entered feedback/i)).toBeNull();

    fireEvent.click(screen.getByRole('tab', { name: 'Privacy' }));
    expect(screen.getByText(/user-entered subject and message as plain text/i)).toBeTruthy();
    expect(screen.getByText(/visible only to its submitter in the active workspace/i)).toBeTruthy();
    expect(screen.getAllByText(/90 days/i).length).toBeGreaterThanOrEqual(1);
  });
});
