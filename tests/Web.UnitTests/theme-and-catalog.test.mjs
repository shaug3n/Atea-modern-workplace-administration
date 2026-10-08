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

test('foundations and components layers load after theme and define shared tokens', async () => {
  const main = await readFile('../../src/Web/src/main.tsx', 'utf8');
  const foundations = await readFile('../../src/Web/src/styles/foundations.css', 'utf8');
  const theme = await readFile('../../src/Web/src/styles/theme.css', 'utf8');
  assert.match(main, /theme\.css[\s\S]*foundations\.css[\s\S]*components\.css/);
  for (const needle of ['--uw-gap-lg', '--uw-status-danger-fg', '--uw-focus-ring', '--uw-header-height: 4rem', '.button--danger-solid', '.checkbox-field']) {
    assert.ok(foundations.includes(needle), `foundations.css should contain ${needle}`);
  }
  assert.doesNotMatch(theme, /\.button-primary \{/);
});

test('table foundations replace the global anchor wrapping and define one pagination and chip rule', async () => {
  const theme = await readFile('../../src/Web/src/styles/theme.css', 'utf8');
  const components = await readFile('../../src/Web/src/styles/components.css', 'utf8');
  assert.doesNotMatch(theme, /\.app-main code, \.app-main a, \.app-main td/);
  assert.match(components, /\.numeric/);
  assert.match(components, /position: sticky/);
  const all = theme + components;
  assert.equal((all.match(/\.pagination-controls \{/g) ?? []).length, 1);
  assert.equal((all.match(/\.filter-chips \{/g) ?? []).length, 1);
});

test('skeleton convention disables shimmer for reduced motion', async () => {
  const components = await readFile('../../src/Web/src/styles/components.css', 'utf8');
  assert.match(components, /\.loading-skeleton[\s\S]*?animation:/);
  assert.match(components, /@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*?\.loading-skeleton[\s\S]*?animation:\s*none/);
});

test('reserved capability names match FE and BE', async () => {
  const frontend = await readFile('../../src/Web/src/capabilities/capabilityTypes.ts', 'utf8');
  const backend = await readFile('../../src/Api/Authorization/Capability.cs', 'utf8');
  const frontendEntries = frontend.match(/export const reservedCapabilities\s*=\s*\[([\s\S]*?)\]\s*as const/)?.[1];
  const backendEntries = backend.match(/public static readonly IReadOnlyList<string> Reserved\s*=\s*\[([\s\S]*?)\];/)?.[1];
  assert.ok(frontendEntries, 'frontend reserved capability list should exist');
  assert.ok(backendEntries, 'backend reserved capability list should exist');

  const frontendValues = [...frontendEntries.matchAll(/'([^']+)'/g)].map(([, value]) => value);
  const backendNames = [...backendEntries.matchAll(/\b([A-Z][A-Za-z0-9]*)\b/g)].map(([, name]) => name);
  const backendConstants = new Map(
    [...backend.matchAll(/public const string ([A-Z][A-Za-z0-9]*)\s*=\s*"([^"]+)";/g)]
      .map(([, name, value]) => [name, value]),
  );
  const backendValues = backendNames.map((name) => {
    assert.ok(backendConstants.has(name), `backend reserved entry ${name} should reference a Capability constant`);
    return backendConstants.get(name);
  });

  assert.deepEqual(frontendValues, [
    'authentication.campaigns.view',
    'authentication.campaigns.manage',
    'licenses.hygiene.view',
    'platform.about.view',
    'feedback.submit',
  ]);
  assert.deepEqual(backendValues, frontendValues);
});
