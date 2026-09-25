# SDD ledger — plan: /private/tmp/atea-workspace-ui-plan.md

## Global constraints
- Implement only on isolated branch `codex/atea-workspace-ui` based on `9cbe37f7df6d3e229556a49ec6f55d35f1a671ea`.
- Preserve Atea brand, current stack, English, light default/dark toggle; no page-level overflow.
- Workspace module grants and signed-in user's Graph delegated permission/Entra RBAC/PIM are separate gates.
- No fabricated metrics. Recovery secret values are never logged or persisted. CSV capped at 10,000 and protected from formula injection.
- Preserve existing data and unrelated files.

## Pre-flight interface scan
| Tasks | Shared interface | Finding |
|---|---|---|
| 1 -> 2 | Shared workspace shell, responsive list patterns, permission states | Task 1 establishes shared layout primitives; Task 2 uses them. No conflict if stable component props are agreed before dispatch. |
| 1 -> 3 | Navigation/routes, responsive data/detail patterns | Task 3 adds device detail route under Task 1 shell. Route ownership must be explicit to avoid editing shared route/nav files simultaneously. |
| 1 -> 4 | Atea tokens, URL filters, table/list patterns | Task 4 should consume shared UI patterns and avoid altering global shell. |
| 2 -> 4 | Users filters and export controls | Task 2 leaves export affordance disabled/clearly explained until endpoint exists; Task 4 wires endpoint without replacing user page behavior. |
| 3 -> 4 | Device query filters and exports | Task 4 export consumes the device search contract; it must not include detail secrets. |
| 1-4 -> 5 | Shared UI/routes/API contracts | Task 5 integrates and documents readiness, no new feature scope. |

## Per-task consistency scan
| Task | Internal consistency |
|---|---|
| 1 | Overview count fix pairs with regression test; responsive shell acceptance covers all named routes. |
| 2 | Detail sections and actions stay within existing user APIs/capabilities; export affordance is staged pending Task 4. |
| 3 | Recovery endpoints, scopes, auditing, no-store and client clear behavior have matching tests; no bulk actions or new workspace grant. |
| 4 | Export and assignee interfaces pair with UI and API tests; cap and truncation are explicit; Graph failure must not yield a silent partial file. |
| 5 | Verification follows all producer tasks; real recovery validation has a declared device prerequisite and contract-test fallback. |

## Decisions
- Ruling: split work into five sequential tasks with disjoint primary ownership — shared shell precedes feature pages and integration — cost if wrong: some later polish may need rework.
- Ruling: use 10,000-row export cap as the explicit safe ceiling — balances requested full filtered export with bounded response cost — cost if wrong: very large tenants need a later async export flow.
- Ruling: show device total as unavailable unless Graph supplies an authoritative count whose scope is validated — prevents false totals or oversharing — cost if wrong: dashboard has fewer visible KPIs until the source is validated.

## Task progress
- Baseline `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --nologo`: passed 267/267.
- Baseline `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --nologo`: passed 171, skipped 1 (real Entra requires credentials). First parallel attempt collided on shared .NET `obj` cache; rerun serially passed.
- Baseline `npm run test:behavior` in `src/Web`: passed 178/178 across 32 files.
- Baseline `npm run test --prefix tests/Web.UnitTests`: passed 4/4.
- Baseline `npm run test --prefix tests/Web.E2E`: 22/24 passed; existing `workspace-access.spec.tsx` has two failures (invitation payload expectation and role-change request expectation). Record as pre-existing and do not fold into this plan unless a task changes that surface.
- Baseline `npm run build` in `src/Web`: passed; existing dynamic-import/chunk-size warnings.
- Baseline `docker compose config -q`: passed.
- `npm ci` in isolated worktree succeeded; reported 2 moderate advisories in locked dependencies, no dependency changes made.
- Task 1 initial implementation commit: `d03bf1f0f9cb2abe2512ad1927308cfab5ae28df`.
- Task 1 review: important issue found in overview attention state; fix dispatched. Minor production-boundary regression test requested and included in fix. Narrow-screen/200% visual QA deferred to Task 5. Existing build warnings recorded for final review.
- Task 1 minor (deferred): baseline/build currently reports existing dynamic-import and >500 KB chunk warnings; assess whether this redesign materially changes bundle size during Task 5.
- Task 1 fix commit: `a1188bff5c6680dddfa63b20e5f9d8ced59e98bc`.
- Task 1 fix review: both findings addressed; no new Critical/Important breakage. Task 1 complete (`9cbe37f..a1188bf`, review clean; responsive browser QA is explicitly deferred to Task 5).
- Task 2 commit: `115037ddbf0c3da913c1a5f4b5c257db9f4a7bab`.
- Task 2 review: spec compliant, no Important/Critical issues. One minor filter disclosure focus issue deferred to Task 5 responsive/interaction review. Narrow/200% browser and assistive-technology checks deferred to Task 5. Task 2 complete (`a1188bf..115037d`, review approved).
- Task 2 minor (deferred): clearing the last character of the license filter closes the optional-filter disclosure while its input has focus. Triage during final UX review.
- Task 3 implementation report: `.superpowers/sdd/atea-workspace-ui-plan/task-3-report.md`.
- Task 3 verification: API unit 287/287; API integration 172 passed, 1 skipped; frontend behavior 202/202; Web.UnitTests 4/4; frontend production build passed with the pre-existing dynamic-import and chunk-size warnings. A final integration run had one PostgreSQL startup read-timeout; the isolated affected test passed and the subsequent serial full suite passed.
- Ruling: keep recovery reveal capability available for deliberate attempts when the required delegated scope is present even if the app's Entra role snapshot is inconclusive — Graph is authoritative for device ownership, custom roles, and administrative-unit scope — cost if wrong: a user may receive a Graph 403 and PIM guidance after trying, rather than being preemptively blocked.
- Task 3 initial implementation commit `36c0a73`; independent review found four important and one minor issue. Review-fix commit `260fe2d` addressed all Important findings; scoped re-review found no Critical/Important regressions. Minor empty-BitLocker-result freshness edge is deferred to Task 5 triage. Task 3 complete after final integration rerun: API unit 305/305, API integration 172 passed/1 skipped, frontend behavior 206/206, Web.UnitTests 4/4, build passed with existing warnings. Browser narrow-screen/200% QA remains deferred to Task 5; real recovery validation requires tenant scopes and an enrolled device with recovery records.
- Task 4 preflight: existing `LicenseOverviewService` incorrectly authorizes the read-only inventory using `licenses.assign`; Task 4 brief now requires switching to `licenses.view` and proving read-only access does not grant assignment.
- Ruling: address the existing license read-capability mismatch within Task 4 because the plan explicitly separates viewing from assignment — prevents over-requiring write privileges for inventory — cost if wrong: a tenant user previously relying on `licenses.assign` to view may need read scopes/capability instead.
- Task 4 report: `.superpowers/sdd/atea-workspace-ui-plan/task-4-report.md`.
- Task 4 verification: API unit 318/318; API integration 177 passed/1 skipped (one initial invitation-audit timeout/failure passed focused rerun and the subsequent full serial run); frontend behavior 208/208; Web.UnitTests 4/4; build passed with the existing warnings; diff check passed.
- Ruling: derive the SKU label from Graph `skuPartNumber` because the subscribed SKU payload selected by the supported Graph contract does not supply a product display-name field — avoids fabricating a product label — cost if wrong: users see technical SKU names rather than polished marketing names until an authoritative catalog mapping is added.
- Task 4 independent review found three Important issues and one Minor issue; review-fix commit `36d3683` addressed all four and scoped re-review verified each, with no new breakage. Task 4 complete at `36d3683`; verification after fixes: API unit 324/324, integration 177 passed/1 skipped, frontend behavior 210/210, Web.UnitTests 4/4, production build passed with existing warnings. E2E/Compose/browser remain Task 5.
- Task 3 independent review at `36c0a73` found stale cross-device metadata, missing source/retrieval freshness, incomplete recovery PIM guidance, missing correlation IDs for a validated-device key miss, and shared metadata error state. One focused fix batch is in progress on the same branch/worktree.
- Ruling: key the device detail by managed-device ID and also guard metadata completions by per-section request generation; this clears the entered recovery reason on navigation and prevents late results from another device or an older reload appearing here. Cost: switching devices remounts the detail UI and resets its local state.
- Ruling: show client-side retrieval time separately for Intune device detail, BitLocker metadata, and Windows LAPS metadata; the device's Graph `lastSyncDateTime` remains a device property, not a fetch timestamp. Cost: retrieval time reflects browser receipt, not a server snapshot timestamp.
- Ruling: derive PIM hints from Microsoft's documented six recovery metadata/BitLocker roles and the two LAPS password roles, while leaving target authorization to Graph. A Global Reader may see LAPS metadata but is not guided to activate that role for password reveal. Cost: custom role or device-owner permissions may still require an attempted Graph request to learn access.
- Task 3 review-fix red tests observed: 3 frontend cases failed for reason reset/source freshness/independent errors; 11 focused API cases failed for role guidance and correlation preservation; later targeted red cases failed for LAPS password explanation and linked, role-specific PIM guidance. Focused green after fixes: 59/59 API tests and 11/11 device-detail behavior tests. Full verification to be recorded before commit.
- Task 3 review-fix final verification: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --nologo --verbosity quiet` passed 305/305; `npm run test:behavior -- --reporter=dot` passed 206/206 across 34 files; `npm run test --prefix tests/Web.UnitTests` passed 4/4; `npm run build` passed with the existing AuthProvider dynamic-import and >500 KB chunk warnings. No integration endpoint contract changed in this fix batch; API integration suite was last green at Task 3 head with 172 passed and 1 skipped.
- Task 4 independent review of `260fe2d..01b6fce` found three Important issues (roster page race, License Administrator read eligibility, malformed Graph user-page collection) and one Minor (roster retry control). All four were handled in a single focused batch; report: `task-4-review-fix-report.md`.
- Ruling: treat an active License Administrator with the delegated read scope as eligible to attempt license inventory and roster reads in the app, while preserving Graph's 403 as final authority. Cost: Graph may still deny an inventory request for that identity, in which case the page shows the authorization error.
- Task 4 review-fix TDD red: five API tests and two frontend tests failed before implementation. Focused green: 51 API tests and seven frontend tests. Full verification: API unit 324/324; API integration 177 passed/1 skipped; frontend behavior 210/210; Web.UnitTests 4/4; build passed with existing warnings. The final test-only extension covering Previous paging passed in the focused seven-test License page suite. `git diff --check` clean. No E2E or Task 5 work in this batch.
- Task 4 review-fix commit: `36d3683` on `codex/atea-workspace-ui`; seven scoped source/test files committed. The isolated worktree is clean and the main checkout remains untouched.
- Task 5 report: `.superpowers/sdd/atea-workspace-ui-plan/task-5-report.md`; module readiness inventory: `docs/module-inventory.md`.
- Task 5 implements shared status/freshness/retry and responsive treatment for Exchange, Activity, General, Modules, and Access; Exchange search/paging/error handling; and closes the deferred optional-filter focus and empty-BitLocker freshness details. Direct Exchange module gating and unsupported routes are covered; future Prism concepts remain absent from live navigation.
- Task 5 independent verification: frontend behavior 220/220, Web.UnitTests 4/4, API unit 324/324, API integration 177 passed/1 skipped, frontend production build passed with the existing AuthProvider dynamic-import and large-chunk warnings, Compose config passed, and `git diff --check` passed. E2E remains at the established 22/24 baseline: the two workspace-access invitation-payload and role-change expectations fail. Browser desktop/narrow/200% inspection was not possible because the Mac was locked. Independent branch review is pending.
- Task 5 commit: `3f1154b`. Independent task review approved with no Critical/Important findings; Minor suggestions were deferred with reasons in the task report. Whole-branch review remains pending.
- Review-fix batch at `3f1154b`: added effective assigned/enabled module routing for user detail, query/page identity gating for license inventory, separate overview access/freshness messaging, and roster responsive labels. Report: `.superpowers/sdd/atea-workspace-ui-plan/review-fix-report.md`.
- Review-fix TDD: red focused assertions reproduced the retained-module, stale-inventory, stale/unavailable-overview, and missing-label defects; focused green passed 38/38. Final frontend behavior passed 225/225 across 36 files, Web.UnitTests passed 4/4, and the Web build passed with existing Vite warnings. No API/.NET or dependency edits.
