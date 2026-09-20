# Task 6 Report: Effective Capability Evaluation and PIM-Aware Guards

Status: implemented, self-reviewed, and committed.

## Summary

- Added string capability/state contracts, versioned stable Entra role-template catalog, and complete workspace capability snapshots.
- Added Graph authorization snapshot reading through the delegated Graph transport boundary from Task 5. The reader resolves the current user, role definitions, active role assignments, eligible PIM assignments, administrative-unit scopes, and safe failure categories.
- Added `CapabilityEvaluator` with fail-closed Microsoft 365 capability decisions:
  - unknown Graph snapshots -> `temporarily_unavailable`
  - missing consent -> `consent_required`
  - missing directory read -> user sections `hidden`
  - Global Reader -> `users.view=allowed` and mutations `read_only`
  - eligible inactive roles -> exact PIM capability states
- Added `GET /api/capabilities` and a workspace-scoped capability guard. The current protected operation is `/api/workspaces/current/consent/start`, guarded by `workspace.settings.manage`.
- Added React capability types, `useCapabilities`, and `PermissionState` for hidden, read-only, disabled, consent, PIM approval/MFA/activation/expired, and temporary unavailable states.
- Added `docs/security/entra-role-capability-matrix.md`.

## TDD Evidence

- Red: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "CapabilityEvaluatorTests|PimStateMapperTests"` initially failed to compile because capability and PIM contracts did not exist.
- Green: the focused API unit tests later passed: 17/17.
- Red/green frontend: `npm run test:behavior -- --run tests/Web.UnitTests/capabilities/PermissionState.test.tsx` first failed on test import path, then hook parsing/mock stability, then passed: 7/7.

## Verification

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` -> passed, 91/91.
- `dotnet build src/Api/Atea.UnifiedWorkplace.Api.csproj` -> passed, 0 warnings, 0 errors.
- `npm run test:behavior` from `src/Web` -> passed, 26/26. Existing test intentionally logs a handled sign-in redirect error to stderr.
- `npm run build` from `src/Web` -> passed. Vite emitted its existing large-chunk warning for a 512.46 kB minified JS chunk.
- `npm test` from `src/Web` -> passed, 0 tests discovered.

## Blocked/Failing Verification

- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --no-restore` is blocked before Task 6 integration tests run:
  - `tests/Api.IntegrationTests/Persistence/WorkspaceRepositoryTests.cs(21,66): error CS1729: 'SkipException' does not contain a constructor that takes 1 arguments`
- `npm test` from `tests/Web.UnitTests` has an existing unrelated failure:
  - `tests/Web.UnitTests/theme-and-catalog.test.mjs`
  - assertion expects `/export type MessageKey/` in `src/Web/src/app/messages.ts`, but that file currently only re-exports `messages`.

## Self-Review

- Confirmed Task 6 files do not log tokens, raw Authorization headers, secrets, or raw Graph payloads.
- Confirmed unrelated dirty local fixes remain unstaged.
- Confirmed Graph mutations remain behind typed Task 5 adapters. The capability guard is a pre-flight denial path only; it does not bypass Graph mutation authority.
- Ruling: because no user-lifecycle mutation endpoint exists yet, the guard is demonstrated on the existing workspace consent-start operation using the platform-only `workspace.settings.manage` capability. Cost if wrong: this guard may need to be moved or duplicated when user lifecycle endpoints are introduced.

## Commit

- Subject: `feat: enforce tenant capabilities and pim states`

## Fix Round 1

Status: implemented; verification captured below.

Reviewer findings addressed:

- Unknown PIM/role status now fails closed. `CapabilityEvaluator` maps unknown or `temporarily_unavailable` PIM requirements to capability state `temporarily_unavailable`, reason `pim_status_unavailable`, and a non-actionable retry next step instead of an activation prompt.
- Platform-only capability guards no longer read delegated Graph authorization snapshots. `workspace.settings.manage` is evaluated from the resolved workspace membership/platform role before any Graph-backed flow is invoked.
- Administrative-unit-scoped active roles no longer satisfy tenant-wide mutation capabilities. A scoped role returns `read_only` with reason `directory_role_scope_not_tenant_wide`.
- Capability guard 403 responses now use `ProblemDetails` while retaining `error`, `capability`, `state`, `reasonCode`, `requiredRole`, and `nextStep` as extensions.

Fix Round 1 TDD evidence:

- Red: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter CapabilityEvaluatorTests` failed for:
  - `Unknown_pim_status_fails_closed_without_actionable_activation_prompt`
  - `Administrative_unit_scoped_active_role_does_not_allow_tenant_wide_mutation`
- Green: same focused evaluator command passed, 11/11.
- Direct endpoint/guard regressions were added to `tests/Api.IntegrationTests/Authorization/CapabilityEndpointTests.cs`, but the integration project still fails to compile before those tests can execute because of the pre-existing `SkipException` blocker below.

Fix Round 1 verification:

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` -> passed, 93/93.
- `dotnet build src/Api/Atea.UnifiedWorkplace.Api.csproj` -> passed, 0 warnings, 0 errors.
- `npm run test:behavior` from `src/Web` -> passed, 26/26. Existing test intentionally logs a handled sign-in redirect error to stderr.
- `npm run build` from `src/Web` -> passed. Vite emitted its existing large-chunk warning for a 512.46 kB minified JS chunk.
- `npm test` from `src/Web` -> passed, 0 tests discovered.

Fix Round 1 blocked/known failing verification:

- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --no-restore` is still blocked before Task 6 integration tests run:
  - `tests/Api.IntegrationTests/Persistence/WorkspaceRepositoryTests.cs(21,66): error CS1729: 'SkipException' does not contain a constructor that takes 1 arguments`
- `npm test` from `tests/Web.UnitTests` still has the unrelated legacy catalog failure:
  - `tests/Web.UnitTests/theme-and-catalog.test.mjs`
  - assertion expects `/export type MessageKey/` in `src/Web/src/app/messages.ts`, but that file currently only re-exports `messages`.

## Fix Round 2

Status: implemented, verified, and ready for commit.

Reviewer findings addressed:

- Missing and blank Graph PIM status now maps to `temporarily_unavailable`; the Graph authorization snapshot reader no longer treats an absent status as `Eligible`. A missing PIM snapshot in evaluator input is also fail-closed and cannot produce an activation prompt.
- Eligible administrative-unit-scoped roles are filtered with the same tenant-wide scope rule as active roles before PIM evaluation. They return `read_only` with reason `directory_role_scope_not_tenant_wide` and no PIM activation next step.

Fix Round 2 TDD evidence:

- Red: the new mapper, snapshot-reader, and AU-scoped eligible-role regressions failed as expected: null/blank/absent status mapped to `activation_required`, and the scoped eligible role returned `pim_activation_required`.
- Green: the focused API command passed, 26/26.

Fix Round 2 verification:

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~PimStateMapperTests|FullyQualifiedName~GraphAuthorizationSnapshotReaderTests|FullyQualifiedName~CapabilityEvaluatorTests" --no-restore` -> passed, 26/26.
- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --no-restore` -> passed, 100/100.
- `dotnet build src/Api/Atea.UnifiedWorkplace.Api.csproj --no-restore` -> passed, 0 warnings, 0 errors.
- `npm run test:behavior` from `src/Web` -> passed, 26/26. Existing unrelated AuthProvider test logs a handled sign-in redirect error to stderr.
- `npm run build` from `src/Web` -> passed. Vite emitted the existing large-chunk warning for a 512.46 kB minified JS chunk.
- `npm test` from `src/Web` -> passed, 0 tests discovered.

Fix Round 2 blocked/known failing verification:

- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --no-restore` remains blocked before integration tests execute:
  - `tests/Api.IntegrationTests/Persistence/WorkspaceRepositoryTests.cs(21,66): error CS1729: 'SkipException' does not contain a constructor that takes 1 arguments`
- `npm test` from `tests/Web.UnitTests` retains the unrelated legacy catalog failure:
  - `tests/Web.UnitTests/theme-and-catalog.test.mjs`
  - assertion expects `/export type MessageKey/` in `src/Web/src/app/messages.ts`, but that file currently only re-exports `messages`.

Fix Round 2 self-review:

- Confirmed only Task 6 source/tests/report changes are intended; unrelated auth/proxy edits remain unstaged.
- Confirmed no tokens, raw Authorization headers, secrets, or raw Graph payloads are logged or returned by the changed paths.
