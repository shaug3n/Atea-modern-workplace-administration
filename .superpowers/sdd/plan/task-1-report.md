# Task 1 implementation report

## Result

Implemented URL-backed built-in user directory views and published the staged specification byte-for-byte. The directory now has accessible All users, Enabled, Disabled, and Guests shortcut tiles; shortcut and advanced-filter changes use browser history entries, live search edits replace the current entry, and popstate restoration resets paging. Search and compatible license filters remain intact, unsupported tenant-role URL state is removed, and the existing search debounce, stale-response guard, paging, export, and authorization behavior remain in place.

Feature-owned typed messages are composed into the application catalog without moving existing user copy. Added regression coverage for shortcut behavior, URL/history semantics, page reset, compatible filters, absence of shortcut/global totals, and message-catalog uniqueness.

## TDD evidence

- **Baseline, before implementation:** `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserFilters.test.tsx ../../tests/Web.UnitTests/features/users/UsersPage.test.tsx` — 2 files, 22 tests passed. `cd tests/Web.UnitTests && node --test --test-reporter=tap theme-and-catalog.test.mjs` — 8 tests passed.
- The first baseline attempt found `vitest` missing. Installed the locked Web dependencies with `cd src/Web && npm ci --silent`; no dependency manifests were changed.
- **RED, before directory implementation:** `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserFilters.test.tsx` — 7 of 10 tests failed as expected because the shortcut controls and history-mode callback were not implemented.
- **RED, before adding feature messages:** `cd tests/Web.UnitTests && node --test --test-reporter=tap theme-and-catalog.test.mjs` — the new catalog regression failed as expected because the feature message module did not exist yet (8 passed, 1 failed).
- **GREEN, final focused tests:** `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserFilters.test.tsx ../../tests/Web.UnitTests/features/users/UsersPage.test.tsx` — 2 files, 29 tests passed.
- **GREEN, full Web.UnitTests checks:** `cd tests/Web.UnitTests && npm test` — 19 tests passed, 0 failed.
- **Build:** `cd src/Web && npm run build` — TypeScript and Vite build succeeded.
- `git diff --check` passed. `cmp` confirmed the published spec matches the staged approved file byte-for-byte.

## Concerns

- No contradictions between the Task 1 brief, approved specification, or feature-extension contracts were found.
- The successful Vite build reports existing warnings about `AuthProvider.tsx` being both dynamically and statically imported, and the generated main chunk exceeding 500 kB. They are unrelated to this change and were not modified.

## Files in scope

- `docs/superpowers/specs/2026-10-08-f2-users-user-360-design.md`
- `src/Web/src/features/users/messages.ts`
- `src/Web/src/features/users/UserFilters.tsx`
- `src/Web/src/features/users/UsersPage.tsx`
- `src/Web/src/messages/en.ts`
- `tests/Web.UnitTests/features/users/UserFilters.test.tsx`
- `tests/Web.UnitTests/features/users/UsersPage.test.tsx`
- `tests/Web.UnitTests/theme-and-catalog.test.mjs`
