import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('web shell renders the workplace title', async () => {
  const source = await readFile('../../src/Web/src/app/messages.ts', 'utf8');
  assert.match(source, /appTitle: 'Atea Unified Workplace'/);
});
