import React, { createContext, useContext, useEffect, useMemo, useState, type KeyboardEvent, type ReactNode } from 'react';
import { useApi } from '../auth/useApi';
import { messages } from '../app/messages';

export type ThemeMode = 'light' | 'dark';

export type ThemePreferenceStore = {
  load: () => Promise<ThemeMode | null>;
  save: (theme: ThemeMode) => Promise<void>;
};

type ThemeContextValue = {
  theme: ThemeMode;
  setTheme: (theme: ThemeMode) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);
let inMemoryThemePreference: ThemeMode | null = null;

export function AppThemeProvider({ children }: { children: ReactNode }) {
  const api = useApi();
  return <ThemeProvider preferenceStore={createApiThemePreferenceStore(api)}>{children}</ThemeProvider>;
}

export function ThemeProvider({ children, preferenceStore = createMemoryThemePreferenceStore(), systemTheme = readSystemTheme }: { children: ReactNode; preferenceStore?: ThemePreferenceStore; systemTheme?: () => ThemeMode }) {
  const [theme, setThemeState] = useState<ThemeMode>(() => systemTheme());

  useEffect(() => {
    let cancelled = false;
    preferenceStore.load()
      .then((preference) => {
        if (!cancelled && preference) {
          setThemeState(preference);
        }
      });
    return () => { cancelled = true; };
  }, [preferenceStore]);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const value = useMemo(() => ({
    theme,
    setTheme: (nextTheme: ThemeMode) => {
      setThemeState(nextTheme);
      void preferenceStore.save(nextTheme);
    },
  }), [preferenceStore, theme]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function ThemeToggle() {
  const { theme, setTheme } = useTheme();
  const darkMode = theme === 'dark';
  const label = darkMode ? messages.disableDarkMode : messages.enableDarkMode;
  const toggle = () => setTheme(darkMode ? 'light' : 'dark');
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      toggle();
    }
  };

  return (
    <button className="theme-toggle" type="button" role="switch" aria-checked={darkMode} aria-label={label} onClick={toggle} onKeyDown={onKeyDown}>
      <span aria-hidden="true">{darkMode ? 'Dark' : 'Light'}</span>
    </button>
  );
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used inside ThemeProvider');
  }
  return context;
}

export function createApiThemePreferenceStore(api: (path: string, init?: RequestInit) => Promise<Response>): ThemePreferenceStore {
  return {
    load: async () => {
      try {
        const response = await api('/api/user-preferences/theme');
        if (!response.ok) {
          return inMemoryThemePreference;
        }
        const body = await response.json() as { theme?: ThemeMode; value?: ThemeMode };
        return body.theme ?? body.value ?? null;
      } catch {
        return inMemoryThemePreference;
      }
    },
    save: async (theme) => {
      inMemoryThemePreference = theme;
      try {
        await api('/api/user-preferences/theme', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ theme }),
        });
      } catch {
        // The development fallback intentionally stores only the theme value.
      }
    },
  };
}

function createMemoryThemePreferenceStore(): ThemePreferenceStore {
  return {
    load: async () => inMemoryThemePreference,
    save: async (theme) => { inMemoryThemePreference = theme; },
  };
}

function readSystemTheme(): ThemeMode {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
