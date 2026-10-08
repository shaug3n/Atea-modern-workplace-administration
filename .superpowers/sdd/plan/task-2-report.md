# Task 2 implementation report

## Result

Implemented the task-2 shared frontend contract, conservative summary mapper,
feature-owned messages, and workspace-scoped access context. No page, route,
shell, API, permission, activation, or dependency-manifest changes were made.

The required `Capability` union addition exposes one pre-existing downstream
exhaustiveness gap outside the task-2-owned files: `src/Web/src/format/humanize.ts`
does not label `workspace.members.manage`. This causes the Web TypeScript build
to fail and one existing full-suite test to fail. That file was intentionally
left untouched to honor the task's file-ownership boundary.

## Interfaces and behavior

- `CapabilitySnapshot.workspaceModules?: WorkspaceModuleEvidence[] | null`.
- `CapabilityDecision.roleEvidence?: CapabilityRoleEvidence | null`.
- `CapabilityRoleEvidence`: `state`, `requiredRoleTemplateIds`,
  `assignments`; each assignment carries `roleTemplateId`, `assignmentState`,
  `scope`, and optional nullable `pimState`.
- Added `workspace.members.manage` to `Capability`.
- `summarizeAccess(snapshot, session)` returns workspace/evaluation/source
  context and module read/write summaries with action-level original API
  decisions, evidence-aware summary states, reason labels, and consent evidence.
- Summary groups cover the 24 current API-evaluated IDs exactly once. Exchange
  remains unavailable because Microsoft-action coverage is incomplete.
- Workspace mismatch, absent snapshot, non-authoritative Graph evidence,
  missing module projection, missing decisions, incomplete role evidence,
  disabled modules, and absent workspace grants remain distinct and fail
  closed. Platform-only decisions remain usable when Graph is unavailable.
- Empty `missingScopes` is described as “The API reported no missing scopes;
  complete consent is not separately shown.” Raw decision role/PIM/next-step
  evidence is retained; no decision is fabricated.
- `AccessTransparencyProvider` loads `/api/capabilities`, scopes exposed
  snapshots to the current workspace, and exposes `snapshot`, `loading`,
  `error`, and awaitable `refresh`. A loader injection supports isolated tests.
- `myAccessMessages` contains only `myAccess`-prefixed feature-owned keys.

## TDD evidence

1. Wrote mapper and reserved-contract tests before production implementation.
   The first run could not find the new summary module, as expected for RED.
2. Wrote context tests while the context file was absent. The focused run
   failed to resolve `accessContext`, confirming the missing behavior.
3. Implemented the contract and mapper; the focused suite passed.
4. Implemented the context; both focused test files passed.

The focused tests cover exact mapping, complete allowed summaries, mixed
outcomes, missing decisions, absent/non-authoritative/mismatched snapshots,
module disabled versus ungranted, owner-inherited effective access, Exchange
coverage, platform-only evidence, empty missing scopes, unavailable role
evidence, context load/refresh, and retry after failure.

## Commands and results

- `npm ci --prefix src/Web` — installed existing lockfile dependencies because
  `vitest` was not present; completed successfully.
- Initial focused command before dependency install:
  `npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/my-access/accessSummary.test.tsx ../../tests/Web.UnitTests/capabilities/reservedContracts.test.tsx`
  — exited 127 (`vitest: command not found`).
- Focused RED run after install — failed to resolve the expected new module(s).
- Focused GREEN command, run again after self-review:
  `npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/my-access/accessSummary.test.tsx ../../tests/Web.UnitTests/capabilities/reservedContracts.test.tsx`
  — **2 files passed, 17 tests passed**.
- `npm run test:behavior --prefix src/Web` — **71 of 72 files passed; 476 of
  477 tests passed**. The remaining existing
  `format/humanize.test.tsx > humanize helpers > maps every Capability member`
  fails because the humanize label table lacks the newly required capability.
- `npm test --prefix tests/Web.UnitTests` — **18 tests passed**.
- `npm run build --prefix src/Web` — TypeScript build fails only at
  `src/format/humanize.ts(4,14)`: the exhaustive `Record<Capability, string>`
  lacks `"workspace.members.manage"`.
- `git diff --check` — passed.

## Changed files

- `src/Web/src/capabilities/capabilityTypes.ts`
- `src/Web/src/features/my-access/accessSummary.ts`
- `src/Web/src/features/my-access/accessContext.tsx`
- `src/Web/src/features/my-access/messages.ts`
- `tests/Web.UnitTests/features/my-access/accessSummary.test.tsx`
- `tests/Web.UnitTests/capabilities/reservedContracts.test.tsx`
- `.superpowers/sdd/plan/task-2-report.md`

The implementation commit SHA is reported in the task handoff.
