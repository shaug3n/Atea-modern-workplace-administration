# F4 Task 4 implementation report

## Delivered

- Added the separate `/licenses/hygiene` page and a route declared with `module: "license-hygiene"` and `capability: "licenses.hygiene.view"`, without sidebar navigation metadata.
- Kept direct-route access fail-closed: the workspace must explicitly enable and assign the hygiene module, and the current-workspace capability decision must be `allowed` or `read_only`.
- Added the authorized-only License inventory link. It appears only when the hygiene capability is allowed/read-only and the hygiene module is explicitly enabled and assigned.
- Added a typed, injectable hygiene loader and `fetchLicenseHygiene`, which requests only `/api/licenses/hygiene`, sets `cache: "no-store"`, and forwards cancellation.
- Added the explainer-first review with independent inventory/user-source status, explicit errors/freshness, observed-scan coverage, unknown unscanned totals, and unsupported inactivity/overlap explanations.
- Added controlled category tiles, search and SKU filters, shared-snapshot KPI/table filtering, two post-filter 25-row pagers, and clear-filters behavior.
- Preserved nullable metadata for unknown assigned SKUs, showing available friendly/original identifiers without inventing names. User-detail links require both Users module grants and `users.view`.
- Workspace changes and refreshes cancel the previous request, clear displayed evidence and filters, and keep the results region `aria-busy` while loading.
- Existing capability/module activation and W0 components were not changed. No dependencies, writes, exports, persistent cache, or new scopes were introduced.

## TDD and verification

### RED

Ran the requested focused command after adding route/page tests and before implementing the feature:

```sh
npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/licenses/LicenseHygienePage.test.tsx ../../tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx ../../tests/Web.UnitTests/app/routeMetadata.test.tsx ../../tests/Web.UnitTests/features/workspace-settings/SettingsDataPages.test.tsx
```

**Result:** exited 1, as expected before implementation. The new page module/route behavior was missing. Subsequent red runs exposed missing SKU identifiers in account rows, incomplete/unavailable zero handling, and test issues around accessible labels; these were corrected before the final green runs.

The workspace-isolation test also caught that filter state survived a workspace switch. Its focused run failed with `expected 'Ada' to be ''`; the page now resets search, SKU, category, and both page numbers when the request scope changes. A final-gate 403 test also failed against the generic service error, so denied HTTP responses now show an explicit permission state and retry action.

### GREEN

Focused behavior tests:

```sh
npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/licenses/LicenseHygienePage.test.tsx ../../tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx ../../tests/Web.UnitTests/app/routeMetadata.test.tsx ../../tests/Web.UnitTests/features/workspace-settings/SettingsDataPages.test.tsx
```

**Result:** passed — 4 test files, 41 tests.

Complete Web behavior suite:

```sh
npm run test:behavior --prefix src/Web
```

**Result:** passed — 72 test files, 483 tests.

Web build/type-check:

```sh
npm run build --prefix src/Web
```

**Result:** passed. Vite reported an ineffective dynamic import for `AuthProvider.tsx` and the existing large-chunk advisory; neither failed the build.

Whitespace check:

```sh
git diff --check
```

**Result:** passed.

## Round 1 review fix

- Updated the partial-scan empty state to say “No matching disabled-account findings in the observed partial scan,” so it does not imply that filters found no underlying findings.
- Added a regression fixture with a partial snapshot containing an account excluded by the search filter; it asserts the filter-scoped message and that partial coverage remains visible. The same test checks the completed-scan empty state remains scoped to current filters.

Focused page tests:

```sh
npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/licenses/LicenseHygienePage.test.tsx
```

**Result:** passed — 1 test file, 12 tests.

Web build:

```sh
npm run build --prefix src/Web
```

**Result:** passed. Existing Vite notices about the ineffective `AuthProvider.tsx` dynamic import and large chunk remain.

Whitespace check:

```sh
git diff --check
```

**Result:** passed.
