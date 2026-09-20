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
