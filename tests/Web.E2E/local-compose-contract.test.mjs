import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

test('local Compose startup supplies Development onboarding and admin configuration', async () => {
  const compose = await readFile('../../docker-compose.yml', 'utf8');
  assert.match(compose, /ASPNETCORE_ENVIRONMENT:\s*\$\{ASPNETCORE_ENVIRONMENT:-Development\}/);
  assert.match(compose, /ConnectionStrings__WorkplaceDb:\s*Host=postgres/);
  assert.match(compose, /Onboarding__PublicBaseUrl:\s*\$\{Onboarding__PublicBaseUrl:-http:\/\/localhost:5173\}/);
  assert.match(compose, /Onboarding__ConsentRedirectUri:/);
  assert.match(compose, /Onboarding__ConsentSigningKey:/);
  assert.match(compose, /PlatformAuthorization__RequiredScope:\s*\$\{PlatformAuthorization__RequiredScope:-platform\.admin\}/);
  assert.match(compose, /AteaAdmin__LocalDevelopment__Enabled:\s*\$\{AteaAdmin__LocalDevelopment__Enabled:-true\}/);
  assert.match(compose, /condition: service_healthy/);
});
