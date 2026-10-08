# F3 Task 02 report

## Result

Implemented only the directory compliance KPI tiles. The Compliant and
Noncompliant cards are now controlled native `KpiFilterTile` buttons; each
toggles only `filters.complianceState`, preserving search and operating-system
filters. The existing filter-change effect still resets pagination. Counts
continue to come from `currentResult.items` and now say **Count on this loaded
page**. The total, last-check-in, conventional filters, clear-filter action,
export, table, and device actions remain in place.

Added feature-owned `devicesMessages` copy and focused behavioral coverage for
combined filters, toggle/clear, conventional-filter synchronization, native
button semantics, `aria-pressed`, and page-scoped counts.

## TDD evidence

Initial baseline attempt:

```text
$ npm --prefix src/Web run test:behavior -- ../../tests/Web.UnitTests/features/devices/DevicesPage.test.tsx
sh: vitest: command not found
```

Installed locked Web dependencies with `npm ci --prefix src/Web`, then reran the
existing test file before adding tests:

```text
Test Files  1 passed (1)
Tests       39 passed (39)
```

**RED** — wrote the two requested behavioral tests first, then ran:

```text
$ npm --prefix src/Web run test:behavior -- ../../tests/Web.UnitTests/features/devices/DevicesPage.test.tsx
```

The two new tests failed as expected because the existing summary cards did not
expose buttons:

```text
DevicesPage > compliance_tiles_toggle_and_clear_only_their_filter
  Unable to find an accessible element with the role "button" and name `/Compliant/`
DevicesPage > compliance_tiles_are_labeled_as_current_page_counts
  Unable to find role="button" and name `/Compliant/`
Test Files  1 failed (1)
Tests       2 failed | 39 passed (41)
```

**GREEN** — after implementing the tiles, the same focused command passed:

```text
Test Files  1 passed (1)
Tests       41 passed (41)
```

## Additional verification

```text
$ npm --prefix src/Web run test:behavior
Test Files  71 passed (71)
Tests       463 passed (463)
```

```text
$ npm --prefix src/Web run build
tsc -b                         passed
vite build                     passed
✓ built in 585ms
```

`git diff --check` passed.

## Notes

- Build emitted Vite warnings about the mixed dynamic/static import of
  `AuthProvider.tsx` and a minified chunk above 500 kB.
- `npm ci` reported 4 dependency audit findings (1 moderate, 1 high, 2
  critical); no dependency manifest or lockfile was changed.
- Tests use mocked device responses and do not claim tenant validation.

## Commit

`feat: add device compliance filter tiles`
