import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('app renders every visible message from the typed catalog', async () => {
  const app = await readFile('../../src/Web/src/app/App.tsx', 'utf8');
  const catalog = await readFile('../../src/Web/src/app/messages.ts', 'utf8');
  assert.match(catalog, /export type MessageKey/);
  assert.match(app, /messages\.appTitle/);
  assert.doesNotMatch(app, />Atea Unified Workplace<\/h1>/);
});

test('baseline provides semantic Atea theme tokens and user dark-mode control', async () => {
  const css = await readFile('../../src/Web/src/app/theme.css', 'utf8');
  const app = await readFile('../../src/Web/src/app/App.tsx', 'utf8');
  assert.match(css, /--color-brand-primary/);
  assert.match(css, /prefers-color-scheme: dark/);
  assert.match(app, /setDarkMode/);
});
