# F7 Task 1 implementation report

## Scope and outcome

Implemented the read-only API evidence projection for `/api/capabilities`. The existing authorization evaluator remains responsible for all decisions; module and role evidence are projected separately after capability evaluation. The endpoint obtains the current workspace's enabled modules through `IWorkspaceSettingsService`.

No frontend, route, policy, permission, Graph-call, scope, write, audit, gate, or reserved-capability activation changes were made.

## TDD evidence

1. Ran the targeted evaluator and endpoint test commands on the clean checkpoint before editing tests: 45 evaluator tests and 5 endpoint tests passed.
2. Added projection and endpoint tests before production changes.
3. RED:
   - The evaluator test project failed to compile because the new `WorkspaceModuleEvidence`, `CapabilityRoleEvidence`, `RoleEvidence`, `WorkspaceModules`, and three-argument evaluator overload did not exist.
   - Endpoint tests failed because the response had no `workspaceModules` projection; the ordinary-member case received 200 but could not find that property.
4. GREEN:
   - Added the optional snapshot/decision evidence records and separate evaluator projection helpers.
   - Passed the focused evaluator and endpoint suites after implementation.
   - A first integration rerun exposed an incorrect test assertion treating `devices.laps.reveal` as reserved. The catalog confirms it is an existing capability, so the test now checks only the reserved capability IDs. The rerun passed.
5. Added a comparison test covering all existing decision fields/states with and without workspace-module evidence; it also verifies absent module evidence remains `null`.

## Verification

Final verification used the repository-required .NET SDK via `$HOME/.dotnet/dotnet`:

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~CapabilityEvaluatorTests`: **50 passed, 0 failed**.
- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~CapabilityEndpointTests`: **7 passed, 0 failed**.
- Full `Api.UnitTests` suite: **368 passed, 0 failed**.
- Full `Api.IntegrationTests` suite: **198 passed, 0 failed, 1 skipped** (`RealEntraValidationTests.RealEntraTokenIsAcceptedAndInvalidTokenIsRejected`, environment-dependent).
- `git diff --check`: passed.

The system `dotnet` executable selected SDK 9.0.318, while `global.json` requires 10.0.401. The required SDK was already installed under the user-local `.dotnet` directory; invoking it explicitly resolved the environment mismatch. No dependencies or project manifests were changed.

## Files changed

- `docs/superpowers/specs/2026-10-08-f7-access-transparency-design.md` — persisted the approved spec byte-for-byte; verified with `cmp`.
- `src/Api/Authorization/CapabilitySnapshot.cs` — additive nullable module and role evidence properties and DTOs.
- `src/Api/Authorization/CapabilityEvaluator.cs` — backward-compatible optional `enabledModules` parameter; separate module and relevant-role evidence projectors. Role projection limits assignments to recognized roles, includes only active/eligible states, emits scope category but no directory-scope ID, and distinguishes unavailable/not-applicable evidence.
- `src/Api/Features/Authorization/CapabilityEndpoints.cs` — reads enabled modules for the caller's workspace and passes them to the evaluator.
- `tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs` — module catalog/owner rules, role filtering/recovery roles/scope privacy, unavailable versus not-applicable evidence, and decision invariance coverage.
- `tests/Api.IntegrationTests/Authorization/CapabilityEndpointTests.cs` — relevant-role and reserved-ID response checks, ordinary member with no module grants, and missing-membership 403.
- `.superpowers/sdd/plan/task-1-report.md` — this report.

## Commits

- Spec-only commit: `84c95bf8809855d3ea7f0c23832db71fe13a3064` (`docs: persist F7 access transparency spec`)
- Implementation commit: created after this report is written.

## Concerns

No implementation blocker or plan defect found. One full-suite integration test remains skipped because it requires real Entra validation credentials/environment; all runnable tests passed.
