# F7 Task 4: Account disclosure and My access route

## Result

Implemented the feature-owned native `DOMAIN ACCESS` disclosure in both existing
desktop and mobile account slots and added `/my-access` as an additive,
non-navigable route. The disclosure summarizes the same Task 2 mapper used by
the page, shows text read/write states, links to My access, and uses the
already-loaded capability state and shared refresh callback.

No extra capability request, workspace/module/manager gate, sidebar metadata,
or permission inference was added. Existing `DomainAccessChip` was not reused:
its contract cannot accurately express mixed, partial, loading, stale, or
unavailable evidence.

## Implementation

- Added `features/my-access/AccountAccessMenu.tsx` and its feature-owned CSS.
  The native `<details>/<summary>` disclosure exposes readable per-module read
  and write labels, preserves mixed/partial evidence, and clearly labels
  initial loading, unavailable evidence, and stale retained evidence.
- Wired the menu into only the existing desktop and mobile account slots in
  `AppShell.tsx`; wrapped the shell and children with the controlled access
  provider.
- Passed App's existing `capabilitiesLoading`, `capabilitiesError`, and
  `refreshCapabilities` state as one small `accessState` prop block.
- Added one `/my-access` route with no module, capability, workspace-access,
  or navigation metadata. It renders the existing page for any signed-in
  session.
- Kept identity/account display, tenant controls, notifications, logout
  behavior, feedback FAB, footer/build chip, and other shell areas unchanged.

## TDD

1. Added account-menu behavior tests, route metadata checks, and shell-level
   navigation tests before implementation.
2. Ran the requested focused test command red: the route metadata test failed
   because `/my-access` was missing; the shell tests failed because the account
   disclosure and route were not implemented; the new menu test could not
   resolve `AccountAccessMenu`.
3. Implemented the route, provider wiring, and disclosure.
4. Re-ran the targeted tests green, then expanded and re-ran them for initial
   loading, failed-refresh stale evidence, mixed/partial labels, ordinary
   member navigation without Graph or workspace grants, shared refresh, and
   mobile drawer closure.

## Verification

- Targeted:
  `npm run test:behavior --prefix src/Web -- --run ../../tests/Web.UnitTests/features/my-access/AccountAccessMenu.test.tsx ../../tests/Web.UnitTests/components/AppShell.test.tsx ../../tests/Web.UnitTests/app/routeMetadata.test.tsx`
  — **3 files passed, 50 tests passed**.
- Full behavior suite:
  `npm run test:behavior --prefix src/Web`
  — **74 files passed, 501 tests passed**.
- Production build:
  `npm run build --prefix src/Web`
  — **passed**. Vite reported the existing dynamic-import notice for
  `AuthProvider.tsx` and the existing large-chunk warning.
- `git diff --check` — passed.

Coverage includes a real account-menu navigation by an ordinary signed-in
member with no manager flags or module grants while the capability loader
fails; matching desktop/mobile disclosure content; Enter/Space open, Escape
close and focus return; refresh via App's existing capability loader; and
mobile drawer close on selection.

## Self-review and scope

Confirmed the route has only `path`, `label`, and `render`; no new module,
capability, workspace-access, or sidebar metadata exists. Both account slots
consume the same context and mapper. Refresh reuses App's callback; it does
not introduce another fetch path. No package manifests, W0 primitives,
authorization behavior, reserved F6 shell areas, or unrelated shell components
were changed.

## Delivery

Conventional Commit subject and commit SHA: recorded in the task handoff.
