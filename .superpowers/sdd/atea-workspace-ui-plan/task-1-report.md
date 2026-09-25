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
