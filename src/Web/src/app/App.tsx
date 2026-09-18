import { useEffect, useState } from 'react';
import { messages } from './messages';
import { OverviewPage } from '../features/overview/OverviewPage';

export function App() {
  const [darkMode, setDarkMode] = useState(false);
  useEffect(() => { document.documentElement.dataset.theme = darkMode ? 'dark' : 'light'; }, [darkMode]);
  return <main data-theme={darkMode ? 'dark' : 'light'}><h1>{messages.appTitle}</h1><button type="button" onClick={() => setDarkMode((enabled) => !enabled)}>{darkMode ? messages.disableDarkMode : messages.enableDarkMode}</button><OverviewPage /></main>;
}
