# Task 2 implementation report

## Result

Implemented the task-2 shared frontend contract, conservative summary mapper,
feature-owned messages, and workspace-scoped access context. No page, route,
shell, API, permission, activation, or dependency-manifest changes were made.

The follow-up correction is recorded at the end of this report. It resolves the
capability-label exhaustiveness gap with the single reserved additive label and
changes the access provider to consume App-owned capability state.

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
- `AccessTransparencyProvider` is controlled by its caller: it receives the
  current `session` and `AccessTransparencyContextValue`, scopes exposed
  snapshots to the current workspace, and forwards `loading`, `error`, and
  awaitable `refresh` without fetching or managing independent state.
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
  477 tests passed** before the follow-up correction. The outstanding failure
  is covered below.
- `npm test --prefix tests/Web.UnitTests` — **18 tests passed** before the
  follow-up correction.
- `npm run build --prefix src/Web` — the initial build exposed the same
  missing `workspace.members.manage` humanize label; the follow-up validation
  below records the corrected result.
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

## Round 1 review corrections

The original access provider owned its own `/api/capabilities` request and
loading/error/refresh lifecycle, duplicating the capability snapshot already
loaded by `App`. It now accepts only the controlled contract:

```tsx
<AccessTransparencyProvider
  session={session}
  value={{
    snapshot: capabilities,
    loading: capabilitiesLoading,
    error: capabilitiesError !== null,
    refresh: refreshCapabilities,
  }}
>
  {children}
</AccessTransparencyProvider>
```

Task 4 must connect the values already supplied by `LoadedWorkspaceExperience`
to that provider (the shown mapping converts App's `Error | null` to the
provider's boolean error state). The provider does not fetch, inject a loader,
create fallback state, or start a parallel loading path. It forwards all
supplied state and the refresh callback; if `value.snapshot.workspaceId` does
not match `session.workspace.id`, it exposes `snapshot: null` while preserving
the other supplied fields. No App integration was added in this task.

The second correction adds exactly one new capability label,
`workspace.members.manage: 'Manage workspace members'`, to
`src/Web/src/format/humanize.ts` and tests that label. No other label keys or
humanize mappings were changed.

### Follow-up evidence

- Reproduced the exhaustive map failure before editing:
  `format/humanize.test.tsx > humanize helpers > maps every Capability member`
  failed because it expected `workspace.members.manage` in
  `CAPABILITY_LABELS`.
- TDD RED run of the focused mapper, reserved-contract, and humanize tests:
  **2 files failed, 1 file passed; 4 tests failed, 18 passed**. Failures were
  the missing workspace member label/map entry and the controlled-provider
  assertions (the prior provider ignored the supplied value).
- Focused GREEN run:
  `npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/my-access/accessSummary.test.tsx ../../tests/Web.UnitTests/capabilities/reservedContracts.test.tsx ../../tests/Web.UnitTests/format/humanize.test.tsx`
  — **3 files passed, 22 tests passed**.
- Full behavior suite after the corrections:
  `npm run test:behavior --prefix src/Web` — **72 files passed, 477 tests
  passed**.
- Production build after the corrections:
  `npm run build --prefix src/Web` — **passed**. Vite emitted the existing
  dynamic-import and large-chunk warnings; TypeScript compilation succeeded.
