import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('frontend auth uses environment configuration and memory-only MSAL cache', async () => {
  const config = await readFile('../../src/Web/src/auth/msalConfig.ts', 'utf8');
  const provider = await readFile('../../src/Web/src/auth/AuthProvider.tsx', 'utf8');
  const api = await readFile('../../src/Web/src/auth/useApi.ts', 'utf8');

  for (const value of ['VITE_ENTRA_CLIENT_ID', 'VITE_ENTRA_AUTHORITY', 'VITE_ENTRA_API_SCOPE', 'VITE_ENTRA_REDIRECT_URI'])
    assert.match(config, new RegExp(value));
  assert.match(config, /cacheLocation:\s*['"]memoryStorage['"]/i);
  assert.doesNotMatch(provider, /graph\.microsoft\.com|refresh_token|localStorage|sessionStorage/i);
  assert.match(api, /Authorization.*Bearer|Bearer.*token/i);
  assert.doesNotMatch(api, /graph\.microsoft\.com/i);
});
