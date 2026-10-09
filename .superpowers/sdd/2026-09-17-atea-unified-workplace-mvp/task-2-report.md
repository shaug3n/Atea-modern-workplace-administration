# Task 2 report: Entra authentication and verified workspace context

## Result

Implemented the Entra authentication boundary and verified workspace context on `codex/atea-unified-workplace-mvp`.

- Microsoft.Identity.Web API bearer validation is configured through `AzureAd` with tenant-independent `organizations` authority settings.
- `/health` is unauthenticated; `/api/session` requires authentication and a server-side workspace membership.
- Workspace resolution uses only verified token `tid` + `oid` claims through `IWorkspaceMembershipReader`.
- Missing workspace membership returns structured `403 {"error":"workspace_membership_required"}`.
- Authentication challenges return structured `401 {"error":"authentication_required"}` without token contents.
- The frontend uses MSAL with environment-only `VITE_` values and memory-only token cache; its API client sends only the API bearer token.
- The production membership implementation is intentionally an empty boundary until Task 3 adapts its repository.

## TDD evidence

### RED

Command:

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~WorkspaceContextTests --disable-build-servers
```

Before the authorization implementation existed, the test project compiled far enough to report missing `Atea.UnifiedWorkplace.Api.Authorization` types, including `AuthenticatedUser`, `IWorkspaceMembershipReader`, and `WorkspaceContextResolution`.

### GREEN

The same focused test command passed after implementation:

```text
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```

The five tests cover missing `tid`, missing `oid`, wrong audience, unknown membership, and a valid customer-tenant identity, asserting `WorkspaceContextFailureReason` values.

## Files changed

- `src/Api/Authorization/AuthenticatedUser.cs`
- `src/Api/Authorization/WorkspaceContext.cs`
- `src/Api/Authorization/WorkspaceContextMiddleware.cs`
- `src/Api/Authorization/PlatformAuthorization.cs`
- `src/Api/Program.cs`
- `src/Api/Atea.UnifiedWorkplace.Api.csproj`
- `src/Api/appsettings.json`
- `src/Api/appsettings.Development.json`
- `src/Web/src/auth/msalConfig.ts`
- `src/Web/src/auth/AuthProvider.tsx`
- `src/Web/src/auth/useApi.ts`
- `src/Web/src/main.tsx`
- `src/Web/src/vite-env.d.ts`
- `src/Web/package.json`
- `src/Web/package-lock.json`
- `tests/Api.UnitTests/Authorization/WorkspaceContextTests.cs`
- `tests/Api.UnitTests/Api.UnitTests.csproj`
- `tests/Api.UnitTests/GlobalUsings.cs`
- `tests/Api.IntegrationTests/Authorization/AuthenticationTests.cs`
- `tests/Api.IntegrationTests/Api.IntegrationTests.csproj`
- `tests/Web.UnitTests/auth/AuthProvider.test.mjs`
- `docs/security/entra-app-registration.md`

## Verification commands and output

API unit suite:

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --disable-build-servers
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```

API integration suite:

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --disable-build-servers
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4
```

Frontend build:

```text
npm run build
✓ 174 modules transformed.
✓ built in 457ms
```

Frontend unit and E2E suites:

```text
npm test --prefix tests/Web.UnitTests
ℹ tests 4
ℹ pass 4
ℹ fail 0

npm test --prefix tests/Web.E2E
ℹ tests 1
ℹ pass 1
ℹ fail 0
```

Additional checks:

- `git diff --check` passed.
- Runtime-source scan found no `access_token`, `refresh_token`, `client_secret`, `Authorization: Bearer`, Graph endpoint, `localStorage`, or `sessionStorage` literals. The forbidden credential strings appear only in deliberate integration-test negative assertions.

## Concerns / follow-up

- `IWorkspaceMembershipReader` currently has an empty production implementation by design; Task 3 must replace it with the EF-backed adapter.
- The appsettings client ID/audience values are non-secret zero placeholders so framework startup remains deterministic. Deployment must override `AzureAd__ClientId` and `AzureAd__Audience`; no production identifiers are committed.
- The frontend build reports Vite's existing large-chunk advisory because MSAL increases the main bundle; this does not fail the build.
- Test commands required escalated local runner/network access in this environment because sandboxed VSTest socket binding and initial NuGet restore were denied.

## Fix round 1

Reviewer findings addressed:

- Resolver identity failures (`tid`, `oid`, and resolver-level audience failures) now produce structured `401 authentication_required`; only a verified identity with no membership produces `403 workspace_membership_required`.
- The API has a fallback authorization policy, so `/api/ping` without endpoint metadata is still protected. `/health` remains explicitly anonymous.
- Added deterministic tests through the actual Microsoft.Identity.Web `Bearer` scheme for valid tokens and wrong audience, wrong issuer, expired, and invalid-signature tokens. The test-only permissive scheme remains isolated to the existing membership/status tests.
- AuthProvider visible strings now come from the typed message catalog. Added behavior tests using React Testing Library/Vitest for sign-in, sign-out, silent token acquisition, interaction-required redirect, sign-in error rendering, and API bearer header behavior.
- Documented the concrete development redirect set (`http://localhost:5173/auth/callback`, `https://localhost:5173/auth/callback`) and production set (`https://workplace.atea.com/auth/callback`) without committing IDs or secrets.
- Added assertions that workspace membership resolution receives the verified tenant and object IDs.

### Fix-round RED evidence

Focused unit run before the resolver classification change failed to compile because `WorkspaceContextResolution.IsAuthenticationFailure` did not exist.

The initial integration run with the new tests failed as expected: malformed identity returned 403 instead of 401, the unannotated `/api/ping` returned 200, and the first valid JWT fixture required deterministic validation configuration.

The first frontend behavior run failed before test collection because the Vitest path/dependency harness was not configured; subsequent runs exposed and fixed React cleanup and rejected-promise handling before reaching a clean pass.

### Fix-round GREEN evidence

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~WorkspaceContextTests --disable-build-servers
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6

DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --disable-build-servers
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11

npm run test:behavior
Test Files  1 passed (1)
Tests       5 passed (5)

npm run build
✓ 174 modules transformed.
✓ built in 493ms

npm test --prefix ../../tests/Web.UnitTests
ℹ tests 3
ℹ pass 3
ℹ fail 0

npm test --prefix ../../tests/Web.E2E
ℹ tests 1
ℹ pass 1
ℹ fail 0
```

Fix-round concerns: `npm install` reports two moderate development-dependency audit findings from the Vitest/jsdom harness, and Vite retains the non-failing MSAL bundle-size advisory. The empty production membership reader remains intentionally reserved for Task 3.

## Fix round 2

Added `RealEntraValidationTests` as an opt-in integration test that uses the production Microsoft.Identity.Web `Bearer` configuration and live OpenID Connect metadata. It reads only these process environment variables: `ATEA_REAL_ENTRA_AUTHORITY`, `ATEA_REAL_ENTRA_TENANT_ID`, `ATEA_REAL_ENTRA_CLIENT_ID`, `ATEA_REAL_ENTRA_AUDIENCE`, and `ATEA_REAL_ENTRA_ACCESS_TOKEN`.

When all variables are present, the test calls `/api/ping` with the supplied real Entra access token and requires a non-401/200 response, then calls the same protected endpoint with an invalid bearer value and requires structured 401. The token is held in memory only and is never logged, persisted, or written into diagnostics. Synthetic JWT tests remain unchanged for deterministic issuer/audience/lifetime/signature coverage.

With the variables absent, the test is skipped during xUnit discovery with the clear reason that all five `ATEA_REAL_ENTRA_*` variables must be set. The real tenant path was not executed in this environment because no test tenant token was supplied; no real Entra IDs, secrets, or tokens were added to the repository.

### Fix-round-2 verification

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~RealEntraValidationTests --disable-build-servers
Passed!  - Failed:     0, Passed:     0, Skipped:     1, Total:     1

DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --disable-build-servers
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6

DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --disable-build-servers
Passed!  - Failed:     0, Passed:    11, Skipped:     1, Total:    12

npm run test:behavior
Test Files  1 passed (1)
Tests       5 passed (5)

npm run build
✓ 174 modules transformed.
✓ built in 74ms

npm test --prefix ../../tests/Web.UnitTests
ℹ tests 3
ℹ pass 3

npm test --prefix ../../tests/Web.E2E
ℹ tests 1
ℹ pass 1
```

## Fix round 3

The real-Entra integration test is now explicitly gated by `ATEA_REAL_ENTRA_RUN=true` in addition to the five documented `ATEA_REAL_ENTRA_*` configuration/token variables. If the boolean gate is absent or not `true`, xUnit skips during discovery with a network-dependency reason and does not start the API host or make metadata requests. If the gate is true but any required value is absent, it skips with the missing-variable reason. The token remains process-memory-only.

### Fix-round-3 verification

Focused opt-in test with the gate and all real-tenant variables absent:

```text
env -u ATEA_REAL_ENTRA_RUN -u ATEA_REAL_ENTRA_AUTHORITY -u ATEA_REAL_ENTRA_TENANT_ID -u ATEA_REAL_ENTRA_CLIENT_ID -u ATEA_REAL_ENTRA_AUDIENCE -u ATEA_REAL_ENTRA_ACCESS_TOKEN DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~RealEntraValidationTests --disable-build-servers
Skipped! - Failed: 0, Passed: 0, Skipped: 1, Total: 1
```

Default API suite with no real-Entra opt-in:

```text
dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --disable-build-servers
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6

dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --disable-build-servers
Passed! - Failed: 0, Passed: 11, Skipped: 1, Total: 12
```

Frontend checks:

```text
npm run test:behavior: 5 passed
npm run build: ✓ 174 modules transformed; ✓ built in 342ms
npm test --prefix ../../tests/Web.UnitTests: 3 passed
npm test --prefix ../../tests/Web.E2E: 1 passed
```
