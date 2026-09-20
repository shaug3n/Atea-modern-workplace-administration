import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { ThemeProvider, ThemeToggle, type ThemePreferenceStore } from '../../../src/Web/src/components/ThemeToggle';

function preferenceStore(initial: 'light' | 'dark' | null = null) {
  const saved: Array<'light' | 'dark'> = [];
  const store: ThemePreferenceStore = {
    load: async () => initial,
    save: async (theme) => { saved.push(theme); },
  };
  return { store, saved };
}

describe('ThemeToggle', () => {
  afterEach(() => {
    cleanup();
    document.documentElement.removeAttribute('data-theme');
  });

  it('uses light mode by default when no user preference or system dark preference exists', async () => {
    const { store } = preferenceStore(null);
    render(<ThemeProvider preferenceStore={store} systemTheme={() => 'light'}><ThemeToggle /></ThemeProvider>);

    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('light'));
    expect(screen.getByRole('switch', { name: 'Enable dark mode' }).getAttribute('aria-checked')).toBe('false');
  });

  it('persists only the selected theme preference when dark mode is enabled', async () => {
    const { store, saved } = preferenceStore(null);
    render(<ThemeProvider preferenceStore={store} systemTheme={() => 'light'}><ThemeToggle /></ThemeProvider>);

    fireEvent.click(await screen.findByRole('switch', { name: 'Enable dark mode' }));

    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('dark'));
    expect(saved).toEqual(['dark']);
  });

  it('labels the keyboard-accessible control with the next action', async () => {
    const { store } = preferenceStore('dark');
    render(<ThemeProvider preferenceStore={store} systemTheme={() => 'light'}><ThemeToggle /></ThemeProvider>);

    const toggle = await screen.findByRole('switch', { name: 'Disable dark mode' });
    expect(toggle.getAttribute('aria-checked')).toBe('true');
    fireEvent.keyDown(toggle, { key: 'Enter' });

    await waitFor(() => expect(screen.getByRole('switch', { name: 'Enable dark mode' }).getAttribute('aria-checked')).toBe('false'));
  });
});
