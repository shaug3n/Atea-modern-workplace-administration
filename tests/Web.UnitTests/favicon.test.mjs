import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const web = new URL('../../src/Web/', import.meta.url);

test('index.html declares an icon so browsers do not request /favicon.ico', () => {
  assert.match(readFileSync(new URL('index.html', web), 'utf8'), /rel="icon"/);
});

test('favicon.svg is shipped from public', () => {
  assert.ok(readFileSync(new URL('public/favicon.svg', web), 'utf8').trimStart().startsWith('<svg'));
});
