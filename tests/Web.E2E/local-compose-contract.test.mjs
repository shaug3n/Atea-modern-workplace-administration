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
  assert.match(compose, /AzureAd__ClientId:\s*\$\{AzureAd__ClientId:-/);
  assert.match(compose, /AzureAd__Audience:\s*\$\{AzureAd__Audience:-/);
  assert.match(compose, /AzureAd__ClientSecret:\s*\$\{AzureAd__ClientSecret:-/);
  assert.match(compose, /AteaAdmin__LocalDevelopment__Enabled:\s*\$\{AteaAdmin__LocalDevelopment__Enabled:-true\}/);
  assert.match(compose, /condition: service_healthy/);
});

test('local user-device guidance identifies the two additional delegated permissions', async () => {
  const runbook = await readFile('../../docs/testing/local-user-device-management.md', 'utf8');
  assert.match(runbook, /User\.RevokeSessions\.All/);
  assert.match(runbook, /DeviceManagementManagedDevices\.PrivilegedOperations\.All/);
});

test('local Compose serves the protected user and device routes without invoking mutations', async () => {
  const associatedDevices = await readFile('../../src/Api/Features/Devices/UserAssociatedDeviceEndpoints.cs', 'utf8');
  const deviceCommands = await readFile('../../src/Api/Features/Devices/DeviceEndpoints.cs', 'utf8');
  assert.match(associatedDevices, /MapGet\("\/api\/users\/\{userObjectId\}\/devices"/);
  assert.match(associatedDevices, /RequireAuthorization\(\)/);
  assert.match(deviceCommands, /MapPost\("\/api\/devices\/\{deviceObjectId\}\/actions\/\{action\}"/);
  assert.match(deviceCommands, /RequireAuthorization\(\)/);
  assert.doesNotMatch(deviceCommands, /HttpClient|graph\.microsoft\.com/i);
});
