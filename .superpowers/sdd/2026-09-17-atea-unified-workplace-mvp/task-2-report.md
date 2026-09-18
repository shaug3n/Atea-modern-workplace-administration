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

