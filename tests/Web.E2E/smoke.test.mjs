import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('web build exposes API health proxy', async () => {
  const config = await readFile('../../src/Web/vite.config.ts', 'utf8');
  assert.match(config, /['"]\/health['"]|\/health/);
});
