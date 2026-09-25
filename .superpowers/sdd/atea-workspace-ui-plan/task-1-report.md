# Task 1 report: shared shell, responsive patterns, overview accuracy

## Outcome

- Added a keyboard accessible mobile menu close action on Escape and returned focus to its toggle. The menu also closes after a route change.
- Added breadcrumbs for nested workspace pages, with links only where a parent route exists. Corrected active navigation so a settings child does not mark its parent as a second current page.
- Tightened shell and overview grid minimum sizes and allowed long links and table content to wrap. Existing responsive table/list reflow remains in the shared stylesheet. Atea assets, theme tokens, light/dark switch, routes, redirects, and module/capability navigation rules remain in place.
- Removed Overview's `/api/devices?pageSize=1` request. `DeviceService` returns `items.Count` as `total`, so that value cannot represent a tenant total. The device summary now explicitly says `Unavailable` and `No verified tenant total`; it never renders an exact device count from the present contract.
- Connection health now loads independently of summary data. Overview uses the API's actual `healthy`/`incomplete` permission states, validates numeric summary values, respects both assigned and enabled modules, and shows a short attention list.

## TDD evidence

Tests were written before each associated production change and run red:

- Device count regression: expected `Unavailable`, received `1Devices`.
- Independent connection loading: connection state was absent while overview loading was unresolved.
- Mobile menu: Escape left `Close menu` expanded.
- Permission health: API `healthy` count `3/4` rendered `Unavailable`.
- Breadcrumb: `/services` was rendered as a link although no such route exists.

Each focused test passed after its implementation change.

## Verification

- `cd src/Web && npm run build`: passed. Existing dynamic import and bundle size warnings remain.
- `cd src/Web && npm run test:behavior`: 32 files, 183 tests passed.
- `npm run test --prefix tests/Web.UnitTests`: 4 tests passed.
- `git diff --check`: passed.
- Self-review: changed files are limited to shared shell/navigation/theme CSS, Overview, and their tests. Task 2–5 feature files were not edited.

## Remaining limits

- The current device API has no authoritative, workspace validated tenant total, so the device card remains unavailable by design. A future exact total requires a dedicated contract with provenance and scope validation.
- Automated tests cover behavior and compilation; a live browser visual pass across narrow widths and both themes was not run in this task.
- The brief identifies two unrelated baseline E2E workspace access failures. The E2E suite was not run because this task did not change workspace access behavior.

## Task 1 review fixes (25 September 2026)

The attention list now treats a verified `incomplete` permission-health state as actionable and includes its count. The production request-boundary test renders authenticated Overview with the real `useApi` hook and an intercepted `fetch`; it proves that the Overview and connection-health requests occur without `/api/devices?pageSize=1`. No other task scope was changed.

### Focused red/green evidence

Command: `cd src/Web && npm run test:behavior -- --run tests/Web.UnitTests/features/overview/OverviewPage.test.tsx`

Red output before the attention fix:

```text
Test Files  1 failed (1)
     Tests  1 failed | 5 passed (6)
× OverviewPage > calls out incomplete permission health in the attention list
→ Unable to find an element with the text: Workspace permissions need attention (3 of 4 available)..
```

Green output after the fix:

```text
✓ ../../tests/Web.UnitTests/features/overview/OverviewPage.test.tsx (6 tests)
Test Files  1 passed (1)
     Tests  6 passed (6)
```

Command: `cd src/Web && npm run test:behavior -- --run tests/Web.UnitTests/features/overview/OverviewRequestBoundary.test.tsx`

The production path already excluded the device request. To verify the new test could catch its return, I temporarily inserted that request, ran the test red, then removed the insertion. Red output:

```text
Test Files  1 failed (1)
     Tests  1 failed (1)
× requests overview and connection health without querying a device search page
→ expected [ …(4) ] to not include '/api/devices?pageSize=1'
```

Command: `cd src/Web && npm run test:behavior -- --run tests/Web.UnitTests/features/overview/OverviewPage.test.tsx tests/Web.UnitTests/features/overview/OverviewRequestBoundary.test.tsx`

Green output with the intended production path restored:

```text
✓ ../../tests/Web.UnitTests/features/overview/OverviewRequestBoundary.test.tsx (1 test)
✓ ../../tests/Web.UnitTests/features/overview/OverviewPage.test.tsx (6 tests)
Test Files  2 passed (2)
     Tests  7 passed (7)
```

### Full verification output

Command: `cd src/Web && npm run test:behavior`

```text
Test Files  33 passed (33)
     Tests  185 passed (185)
```

Command: `cd src/Web && npm run build`

```text
✓ 241 modules transformed.
[INEFFECTIVE_DYNAMIC_IMPORT] src/auth/AuthProvider.tsx is dynamically imported by src/main.tsx but also statically imported by src/app/App.tsx, src/auth/useApi.ts, src/features/invitations/InvitationRedemptionPage.tsx, dynamic import will not move module into another chunk.
(!) Some chunks are larger than 500 kB after minification.
✓ built in 297ms
```

Command: `npm run test --prefix tests/Web.UnitTests`

```text
ℹ tests 4
ℹ suites 0
ℹ pass 4
ℹ fail 0
ℹ cancelled 0
ℹ skipped 0
ℹ todo 0
```

Build warnings are the pre-existing dynamic-import and bundle-size warnings. The E2E suite was not run; the review fixes do not touch workspace access behavior.
