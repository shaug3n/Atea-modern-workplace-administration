import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('users directory browser client uses the API boundary only', async () => {
  const apiSource = await readFile('../../src/Web/src/features/users/usersApi.ts', 'utf8');
  const pageSource = await readFile('../../src/Web/src/features/users/UsersPage.tsx', 'utf8');
  const tableSource = await readFile('../../src/Web/src/features/users/UsersTable.tsx', 'utf8');

  assert.match(apiSource, /\/api\/users/);
  assert.doesNotMatch(`${apiSource}\n${pageSource}\n${tableSource}`, /graph\.microsoft\.com/i);
  assert.match(pageSource, /continuationToken/);
  assert.match(tableSource, /users\.disable/);
});
