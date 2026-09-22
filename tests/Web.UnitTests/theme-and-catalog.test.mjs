import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('app renders every visible message from the typed catalog', async () => {
  const app = await readFile('../../src/Web/src/app/App.tsx', 'utf8');
  const catalog = await readFile('../../src/Web/src/app/messages.ts', 'utf8');
  const shell = await readFile('../../src/Web/src/components/AppShell.tsx', 'utf8');
  assert.match(catalog, /export type MessageKey/);
  assert.match(shell, /messages\.appTitle/);
  assert.match(app, /AppShell/);
  assert.doesNotMatch(shell, />Atea Unified Workplace<\/h1>/);
});

test('baseline provides semantic Atea theme tokens and user dark-mode control', async () => {
  const css = await readFile('../../src/Web/src/styles/theme.css', 'utf8');
  const app = await readFile('../../src/Web/src/app/App.tsx', 'utf8');
  const toggle = await readFile('../../src/Web/src/components/ThemeToggle.tsx', 'utf8');
  assert.match(css, /--uw-primary/);
  assert.match(css, /:root\[data-theme='dark'\]/);
  assert.match(app, /ThemeProvider|AppThemeProvider/);
  assert.match(toggle, /role="switch"/);
});

test('theme defines semantic action and spacious detail card classes', async () => {
  const css = await readFile('../../src/Web/src/styles/theme.css', 'utf8');
  assert.match(css, /\.button--primary/);
  assert.match(css, /\.page-action-bar/);
  assert.match(css, /\.detail-card/);
});
