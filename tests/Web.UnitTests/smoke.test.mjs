import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('web shell renders the workplace title', async () => {
  const source = await readFile('../../src/Web/src/app/App.tsx', 'utf8');
  assert.match(source, /Atea Unified Workplace/);
});
