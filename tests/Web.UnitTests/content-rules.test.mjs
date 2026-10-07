import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const src = fileURLToPath(new URL('../../src/Web/src/', import.meta.url));

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? walk(path) : [path];
  });
}

const sources = ['features', 'components'].flatMap(dir => walk(join(src, dir))).filter(file => file.endsWith('.tsx')).map(file => ({ file, text: readFileSync(file, 'utf8') }));

function offenders(pattern) {
  return sources.filter(({ text }) => pattern.test(text)).map(({ file }) => file.replace(src, ''));
}

test('dates go through the shared formatters, not toLocaleString', () => {
  assert.deepEqual(offenders(/toLocale(Date)?String\(/), []);
});

test('generic confirmation copy is gone', () => {
  assert.deepEqual(offenders(/Confirm action|userMutationConfirm/), []);
});

test('page-level regions do not duplicate the page heading', () => {
  assert.deepEqual(offenders(/aria-label="(License inventory|Device details)"/), []);
});

test('legacy AsyncState component stays removed', () => {
  assert.equal(existsSync(join(src, 'components/AsyncState.tsx')), false);
});

test('overflow-wrap: anywhere is limited to an allow-list of selectors', () => {
  const css = readFileSync(join(src, 'styles/theme.css'), 'utf8');
  // Remaining uses are for identifiers, technical values and admin/legacy tables that must wrap rather than overflow.
  const allowed = /admin|code|dd\b|dd,|detail|recovery|exchange|audit-reference|workspace-partial-notice|licenses-page|license-identifiers|module-inline|users-page|user-status-strip|technical|copy-value/i;
  const blocks = css.split('}').filter(block => /overflow-wrap:\s*anywhere/.test(block));
  const bad = blocks.map(block => block.split('{')[0].trim()).filter(selector => !allowed.test(selector));
  assert.deepEqual(bad, []);
});
