import { cleanup, render } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, formatRelative } from '../../../src/Web/src/format/dateTime';
import { DateTime } from '../../../src/Web/src/components/DateTime';

const now = Date.parse('2026-10-07T12:00:00Z');
const ago = (ms: number) => new Date(now - ms).toISOString();

describe('formatRelative', () => {
  afterEach(cleanup);
  it('formats recent times with short relative text', () => {
    expect(formatRelative(ago(30_000), now)).toBe('just now');
    expect(formatRelative(ago(3 * 60_000), now)).toBe('3 min ago');
    expect(formatRelative(ago(2 * 3_600_000), now)).toBe('2 h ago');
    expect(formatRelative(ago(30 * 3_600_000), now)).toBe('yesterday');
  });

  it('falls back to an absolute date for older values', () => {
    const iso = ago(10 * 86_400_000);
    expect(formatRelative(iso, now)).toBe(formatDate(iso));
  });

  it('never renders Invalid Date or NaN', () => {
    for (const value of ['garbage', null, undefined, '']) {
      expect(formatRelative(value as string, now)).toBe('—');
    }
    expect(formatDate(undefined)).toBe('—');
    expect(formatDateTime('nope')).toBe('—');
  });

  it('shows an absolute timestamp for future values', () => {
    const iso = new Date(now + 2 * 3_600_000).toISOString();
    expect(formatRelative(iso, now)).toBe(formatDateTime(iso));
  });

  it('renders a time element with datetime and a title holding the other form', () => {
    const { container } = render(<DateTime value="2026-10-07T10:00:00Z" />);
    const time = container.querySelector('time')!;
    expect(time.getAttribute('datetime')).toBe('2026-10-07T10:00:00Z');
    expect(time.getAttribute('title')).toBeTruthy();
    const { container: invalid } = render(<DateTime value="garbage" />);
    expect(invalid.querySelector('time')).toBeNull();
    expect(invalid.textContent).toBe('—');
  });
});
