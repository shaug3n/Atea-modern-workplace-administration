# Task 5: Owned E2E and validation report

## Scope and result

Added `tests/Web.E2E/my-access.spec.tsx` using the existing Vitest/jsdom harness and the real `App`, router, account disclosure, shared access provider, and injected session/capability loaders. No production feature code required changes.

Coverage:

- Ordinary non-manager member reaches My access from desktop and mobile account disclosures.
- A failed capability load can be retried; the updated page and account summary consume the same loaded snapshot.
- On workspace switch, the prior snapshot disappears; retry then supplies and displays only a matching workspace snapshot.
- Graph-unavailable and unmapped Exchange read/write evidence stay unavailable; page refresh uses the injected loader without `fetch` calls or writes.

## TDD evidence

- Initial RED exposed test-harness assumptions, not a production regression: the first fixture route used `/overview`, whose current route does not forward the injected connection-health loader and therefore entered the authenticated API path. The fixture starts at `/identity` instead. Other early failures were corrected accessible-name/text queries based on the rendered UI.
- A focused RED for workspace matching failed at `Snapshot workspace ID:` after retry because the fixture continued returning `workspace-1`. The loader fixture was then changed to return `workspace-2` on retry. The final test proves the old snapshot remains hidden before the matching snapshot arrives and that the matching workspace is displayed afterward.
- No implementation change was made; no feature bug was exposed.

## Validation

Final commands below exited 0. They were rerun serially after parallel verification briefly contended for shared test/build resources.

| Command | Result |
| --- | --- |
| `npm test --prefix tests/Web.E2E -- my-access.spec.tsx` | 6 Node contract checks passed; focused Vitest: 1 file, 5 tests passed. |
| `$HOME/.dotnet/dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~CapabilityEvaluatorTests` | 50 passed, 0 failed, 0 skipped. |
| `$HOME/.dotnet/dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~CapabilityEndpointTests` | 7 passed, 0 failed, 0 skipped. |
| `npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/my-access/accessSummary.test.tsx ../../tests/Web.UnitTests/features/my-access/MyAccessPage.test.tsx ../../tests/Web.UnitTests/features/my-access/AccountAccessMenu.test.tsx ../../tests/Web.UnitTests/components/AppShell.test.tsx ../../tests/Web.UnitTests/app/routeMetadata.test.tsx` | 5 files, 75 tests passed. |
| `npm test --prefix tests/Web.UnitTests` | 18 Node tests passed. |
| `npm test --prefix tests/Web.E2E` | 6 Node contract checks passed; 12 Vitest files, 33 tests passed. |
| `npm run build --prefix src/Web` | TypeScript and Vite build succeeded. |
| `git diff --check` | Passed; no whitespace errors. |

Build emitted non-failing warnings: `INEFFECTIVE_DYNAMIC_IMPORT` for `AuthProvider.tsx`, the main bundle exceeding 500 kB, and a CSS transform/plugin timing diagnostic.

The parallel verification attempt itself produced three transient failures: the focused desktop journey timed out at 5 seconds; one existing `AppShell.test.tsx` Devices assertion observed no `/api/devices` call; and the API integration build could not access `src/Api/obj/Debug/net10.0/rjsmrazor.dswa.cache.json` while another process held it. The focused journey, focused Web behavior suite, full Web unit command, and API integration suite were rerun serially and passed. No product or test code changed in response to those transient failures.

## Local UI inspection

Inspected the actual local `App` with a temporary safe fixture supplying an ordinary-member session and local capability snapshot; this was browser inspection, not a live-auth or backend test.

- Desktop at 1440×960: opened the account disclosure by pointer; exercised disclosure open/close and Escape focus return, visible focus rings, and My access link navigation by keyboard. Read/write labels and status text remain visible beside color-coded status indicators.
- Narrow at 390×844: inspected the page and account disclosure with pointer and keyboard, navigated to My access by keyboard, and verified the read/write groups reflow into one column. Measured document width 390 px at a 390 px viewport (no horizontal overflow).
- Captures were written by the browsing tool under `/Users/sondre.haugen/Library/Caches/superpowers/browser/2026-10-08/session-1791469327504` (desktop `005`, `006`, `009`, `013`; narrow `014`–`017`, `053`–`056`). No external Prism site, live Graph, Entra authentication, or credential UI was accessed. These captures do not establish live-service behavior.
- The first Vite start lacked required MSAL environment settings; the preview was restarted with dummy, non-credential local values. Browser history navigation in this temporary page can fall back to the regular SPA if reloaded, so only the in-page injected fixture flow was inspected.

## Cleanup and review

- Removed temporary `src/Web/task5-preview.html` and `src/Web/task5-preview.tsx`; only the owned E2E test is intended as a source change.
- Stopped the task-owned Vite server. Port 5173 was subsequently observed serving a separate `shaug3n-stunning-lamp` worktree; it was not modified or stopped.
- Self-review found no changes to W0, API behavior, authorization, or shared shell code. No PR or rebase was performed.
