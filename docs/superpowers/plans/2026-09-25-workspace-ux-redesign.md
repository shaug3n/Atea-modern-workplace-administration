# Workspace UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the customer workspace clear, responsive, and accessible while preserving delegated Microsoft authorization and existing management workflows.

**Architecture:** Add a small React workspace presentation layer for navigation, notifications, page structure, and responsive results. Existing feature pages keep their data endpoints and actions. The API only gains a deterministic license-name resolver; Graph and workspace authorization remain server-enforced.

**Tech Stack:** React, TypeScript, Vite/Vitest, CSS, .NET 9, xUnit, PostgreSQL-backed existing API.

**Spec:** `docs/superpowers/specs/2026-09-25-workspace-ux-redesign-design.md`

## Global Constraints

- The API remains the authority for workspace module grants, delegated Graph scopes, Entra RBAC, and active PIM state; frontend affordances cannot grant access.
- Keep the existing Atea logo, light default, dark toggle, current API response fields, and local-first deployment model.
- Consolidate workspace CSS rules being changed instead of appending another override layer; leave separate platform-admin selectors intact.
- No new Graph scope, tenant mutation, database migration, persisted notification inbox, or Atea platform-console redesign.
- Do not expose tokens, recovery values, raw Graph errors, or user records in notification state or logs.
- Keep the user-owned untracked files in the main checkout untouched, including `tests/Web.E2E/pim-activation.spec.ts`.
- Existing `/settings/*`, `/workspace-settings`, `/workspace-access`, and `/onboarding` links must reach the corresponding section of `/settings`; invitation and consent flows still work.

## File structure and ownership

| Unit | Files | Responsibility |
| --- | --- | --- |
| Shell and navigation | `src/Web/src/components/PrimaryNav.tsx`, `AppShell.tsx`, `WorkspacePageHeader.tsx`, `src/Web/src/styles/theme.css` | Clear hierarchy, disclosure navigation, header action placement, semantic spacing. |
| Notifications | `src/Web/src/notifications/workspaceIssues.ts`, `WorkspaceNotifications.tsx`, `src/Web/src/components/NotificationsMenu.tsx` | Normalize and group safe capability/health/page issues; render accessible header dropdown. |
| Routing and gates | `src/Web/src/app/App.tsx`, `routes.tsx`, `src/Web/src/capabilities/useCapabilities.ts` | Preserve module boundaries, permit read-only views, refresh capability state, support settings section deep links. |
| Settings | `src/Web/src/features/workspace-settings/WorkspaceSettingsHub.tsx` and existing section pages | One settings page; independent authorized sections with existing APIs. |
| Result views | `src/Web/src/components/ResponsiveDataView.tsx`, `WorkspaceDataState.tsx`, feature pages and `theme.css` | Shared desktop table/narrow summary pattern and stable data states. |
| License labels | `src/Api/Features/Licenses/LicenseDisplayNameResolver.cs`, `GraphLicenseOverviewReader.cs`, existing license page | Known English SKU names with explicit unknown-code fallback. |

## Review Focus

1. A workspace switch during a pending issue report must discard the old workspace's issues; Task 2 tests this.
2. A Global Reader with `read_only` view decisions must see authorized data while mutations remain unavailable; Tasks 3 and 5 test this.
3. A non-admin opening a notification about missing consent must see contact guidance, never an inaccessible settings link; Task 2 tests this.
4. A legacy settings URL or notification deep link must open and focus the correct authorized section, including after refresh; Task 4 tests this.
5. An unfamiliar or unusually long SKU part number must remain identifiable without a fabricated product label or page overflow; Task 6 tests this.

---

### Task 1: Workspace shell and navigation hierarchy

**Files:** Modify `src/Web/src/components/PrimaryNav.tsx`, `AppShell.tsx`, `src/Web/src/styles/theme.css`; create `src/Web/src/components/WorkspacePageHeader.tsx`; test `tests/Web.UnitTests/components/PrimaryNav.test.tsx`, `AppShell.test.tsx`, `WorkspacePageHeader.test.tsx`.

**Interfaces:** Produce `WorkspacePageHeader({ title, description?, eyebrow?, actions?, children? }: WorkspacePageHeaderProps)` and a `PrimaryNav` that accepts its current props. Later tasks use this header and the existing `AppShell` contract.

- [ ] **Step 1: Write failing component tests.** Assert People and Services are disclosure buttons with `aria-expanded`; sublinks remain keyboard reachable when expanded; only one Workspace Settings link is present; Graph denial does not remove an assigned module; unassigned modules stay absent; active state is accurate. Assert the page header renders title before a named primary action.
- [ ] **Step 2: Run the focused tests.** Run `npm --prefix src/Web run test:behavior -- PrimaryNav.test.tsx AppShell.test.tsx WorkspacePageHeader.test.tsx`; expect the new assertions to fail.
- [ ] **Step 3: Implement the shell.** Use local disclosure state in `PrimaryNav`; keep groups expanded when they contain the active route. Put the page heading/action layout in `WorkspacePageHeader`; revise only workspace selectors in `theme.css`, preserving the platform admin shell.
- [ ] **Step 4: Re-run focused tests.** Run the focused command above; expect pass.
- [ ] **Step 5: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 6: Commit the shell task.** Stage only listed task files and commit `feat: simplify workspace navigation`.

### Task 2: Unified notification model and dropdown

**Files:** Create `src/Web/src/notifications/workspaceIssues.ts`, `WorkspaceNotifications.tsx`, `src/Web/src/components/NotificationsMenu.tsx`; modify `AppShell.tsx`, `App.tsx`, `src/Web/src/capabilities/useCapabilities.ts`, `src/Web/src/styles/theme.css`; test `tests/Web.UnitTests/notifications/workspaceIssues.test.tsx`, `NotificationsMenu.test.tsx`, `tests/Web.UnitTests/components/AppShell.test.tsx`.

**Interfaces:** Produce `WorkspaceIssue` with `key`, `area`, `kind: 'setup' | 'access' | 'service'`, `severity: 'warning' | 'info'`, `title`, `detail`, optional `action` and `correlationId`; `deriveCapabilityIssues(snapshot: CapabilitySnapshot | null, session: AppSession): WorkspaceIssue[]`; `WorkspaceNotificationsProvider({ session, capabilities, capabilitiesError, onRefresh, children })`; `useWorkspaceIssueReporter()` with `report(issue: WorkspaceIssue)` and `clear(key: string)`. Outside a provider, the reporter is a no-op so standalone feature tests still render. Feature pages use the reporter in later tasks. The provider loads existing connection health and merges its safe status with capability and reported page issues.

- [ ] **Step 1: Write failing issue-model tests.** Assert same-cause capability decisions collapse into one item per affected area; `read_only` items appear in Your access but do not increase warning count; PIM activation keeps its available action; unknown scope evidence says verification is unavailable; a non-admin consent item has contact guidance and no settings URL; changing `workspace.id` drops reported issues.
- [ ] **Step 2: Write failing dropdown tests.** Assert labelled control, grouped issue titles, warning count, keyboard opening, Escape/focus return, and manual refresh. No raw API error text should appear.
- [ ] **Step 3: Run focused tests.** Run `npm --prefix src/Web run test:behavior -- workspaceIssues.test.tsx NotificationsMenu.test.tsx`; expect failures.
- [ ] **Step 4: Implement normalization, provider, and menu.** Keep only safe categories in state; load `/api/workspaces/current/connection-health`; add explicit capability refresh in both API and injected capability hooks; mount the provider around the shell and page in `App.tsx`. Refresh after retry/consent/PIM return or menu action and clear issues after success.
- [ ] **Step 5: Re-run notification and shell tests.** Run `npm --prefix src/Web run test:behavior -- workspaceIssues.test.tsx NotificationsMenu.test.tsx AppShell.test.tsx`; expect pass.
- [ ] **Step 6: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 7: Commit the notification task.** Commit `feat: centralize workspace notifications`.

### Task 3: Stable Graph permission pages and route gates

**Files:** Modify `src/Web/src/app/App.tsx`, `routes.tsx`, `src/Web/src/components/PermissionState.tsx`; create `src/Web/src/components/WorkspaceDataState.tsx`; test `tests/Web.UnitTests/components/AppShell.test.tsx`, `tests/Web.UnitTests/capabilities/PermissionState.test.tsx`, `tests/Web.UnitTests/components/WorkspaceDataState.test.tsx`.

**Interfaces:** Produce `WorkspaceDataState({ state: 'loading' | 'empty' | 'unavailable', message, onRetry? })`. Route rendering treats `allowed` and `read_only` as viewable; a denied Graph view renders its page frame without loading protected data. Workspace membership and module gates keep their existing denial semantics.

- [ ] **Step 1: Write failing route tests.** Assert `users.view: read_only` renders Users; `users.view: consent_required` retains the Users heading and stable unavailable region; unavailable capability checks do not blank settings or other workspace shell content; an unassigned module still blocks direct URL access.
- [ ] **Step 2: Run focused tests.** Run `npm --prefix src/Web run test:behavior -- AppShell.test.tsx PermissionState.test.tsx WorkspaceDataState.test.tsx`; expect failures.
- [ ] **Step 3: Implement route and state handling.** Keep the existing backend gates; do not fetch protected data under denied/unknown view decisions. Use `WorkspaceDataState` instead of a large route-level permission panel for Graph issues; report the safe cause to Task 2's provider. Preserve contextual disabled-action semantics without duplicating long diagnostic copy inside the page.
- [ ] **Step 4: Re-run focused tests.** Run the focused command above; expect pass.
- [ ] **Step 5: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 6: Commit the route task.** Commit `fix: keep workspace pages stable during Graph errors`.

### Task 4: One Workspace Settings page with deep links

**Files:** Create `src/Web/src/features/workspace-settings/WorkspaceSettingsHub.tsx`; modify `OnboardingPage.tsx`, `WorkspaceSettingsPage.tsx`, `WorkspaceModulesPage.tsx`, `src/Web/src/features/workspace-access/WorkspaceAccessPage.tsx`, `src/Web/src/app/App.tsx`, `routes.tsx`, `src/Web/src/styles/theme.css`; test `tests/Web.UnitTests/features/workspace-settings/WorkspaceSettingsHub.test.tsx`, `SettingsDataPages.test.tsx`, `tests/Web.UnitTests/components/customer-workspace-redesign.test.tsx`, `tests/Web.E2E/workspace-access.spec.tsx`.

**Interfaces:** Produce `WorkspaceSettingsHub({ session }: { session: AppSession })` at `/settings`. Existing section components accept optional `embedded?: boolean` and render a section heading rather than a second page title when embedded. `navigate(destination)` accepts same-origin paths with optional `#connection`, `#general`, `#modules`, or `#access`, while route matching uses the pathname.

- [ ] **Step 1: Write failing settings tests.** Assert one page heading, four independent sections for an owner, Access visibility for `canManageMembers` without `canManageSettings`, a failed Modules request leaving General and Access available, and a member seeing no unauthorized section. Assert each legacy path and refreshable hash link focuses the intended section.
- [ ] **Step 2: Run focused tests.** Run `npm --prefix src/Web run test:behavior -- WorkspaceSettingsHub.test.tsx SettingsDataPages.test.tsx customer-workspace-redesign.test.tsx`; expect new assertions to fail.
- [ ] **Step 3: Implement the hub and routing.** Reuse existing API-backed section components with `embedded`; keep separate loading/saving states and ownership confirmations. Put long delegated-scope guidance behind a labelled disclosure in Connection. Route old paths to `/settings#section` and preserve consent callback behavior. Update the outdated workspace-access E2E expectations to match required module selections and owner-only administrator grants without weakening the UI or API rules.
- [ ] **Step 4: Re-run focused settings tests.** Run the focused command above; expect pass.
- [ ] **Step 5: Run the workspace-access E2E file.** Run `node src/Web/node_modules/vitest/vitest.mjs run --config tests/Web.E2E/vitest.config.ts workspace-access.spec.tsx`; expect pass.
- [ ] **Step 6: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 7: Commit the settings task.** Commit `feat: unify workspace settings` with only task files.

### Task 5: Responsive results for Users and Devices

**Files:** Create `src/Web/src/components/ResponsiveDataView.tsx`; modify `src/Web/src/features/users/UsersPage.tsx`, `UsersTable.tsx`, `src/Web/src/features/devices/DevicesPage.tsx`, `DeviceDetailPage.tsx`, `src/Web/src/styles/theme.css`; test `tests/Web.UnitTests/components/ResponsiveDataView.test.tsx`, `tests/Web.UnitTests/features/users/UsersTable.test.tsx`, `UsersPage.test.tsx`, `tests/Web.UnitTests/features/devices/DevicesPage.test.tsx`, `DeviceDetailPage.test.tsx`.

**Interfaces:** Produce generic `ResponsiveDataView<T>({ items, keyOf, label, renderTable, renderCompact }: ResponsiveDataViewProps<T>)`. Desktop uses a semantic table; narrow mode uses a labelled summary/detail list for the same records. Existing pagination, action dialogs, and API fetchers remain unchanged. Feature pages call Task 2's `useWorkspaceIssueReporter` on sanitized failed/unavailable reads and clear it after successful reads.

- [ ] **Step 1: Write failing data-view and feature tests.** Assert primary fields and named row actions in desktop and compact modes; long UPN/device identifiers wrap; reader data remains visible with no mutation control; a failed Graph read keeps title/filters and reports one service issue; a successful retry clears that issue.
- [ ] **Step 2: Run focused tests.** Run `npm --prefix src/Web run test:behavior -- ResponsiveDataView.test.tsx UsersTable.test.tsx UsersPage.test.tsx DevicesPage.test.tsx DeviceDetailPage.test.tsx`; expect new assertions to fail.
- [ ] **Step 3: Implement shared results and adapt both modules.** Reduce primary columns, move secondary fields into row details, place actions beside their record or in an accessible menu, and apply `WorkspacePageHeader`/`WorkspaceDataState`. Preserve device recovery reveal controls and destructive confirmations.
- [ ] **Step 4: Re-run focused tests.** Run the focused command above; expect pass.
- [ ] **Step 5: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 6: Commit the result-view task.** Commit `feat: refine user and device workspace views`.

### Task 6: Friendly license labels and license view

**Files:** Create `src/Api/Features/Licenses/LicenseDisplayNameResolver.cs`; modify `src/Api/Infrastructure/Graph/GraphLicenseOverviewReader.cs`, `src/Web/src/features/licenses/LicensesPage.tsx`, `src/Web/src/styles/theme.css`; test `tests/Api.UnitTests/Licenses/LicenseInventoryTests.cs`, `tests/Api.IntegrationTests/Licenses/LicenseEndpointTests.cs`, `tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx`.

**Interfaces:** Produce `public static string? LicenseDisplayNameResolver.Resolve(string partNumber)`; `null` means unknown. Preserve `LicenseOverviewItem` fields and API JSON shape. `displayName` uses the known label or the original code; the UI marks an unknown code as “Product name unavailable” while retaining `partNumber` and `skuId`. Existing server search already checks `DisplayName`, `PartNumber`, and `SkuId`. Initial mappings, verified against [Microsoft's licensing reference](https://learn.microsoft.com/en-us/entra/identity/users/licensing-service-plan-reference): `SPE_E5` → `Microsoft 365 E5`, `SPE_E3` → `Microsoft 365 E3`, `DEVELOPERPACK_E5` → `Microsoft 365 E5 Developer (without Windows and Audio Conferencing)`, `ENTERPRISEPREMIUM` → `Office 365 E5`, `ENTERPRISEPACK` → `Office 365 E3`, `VISIOCLIENT` → `Visio Plan 2`. Check additional SKUs actually seen in the authorized test tenant against the same reference before adding them. Correct the affected license-reader fixture that currently calls `ENTERPRISEPACK` Microsoft 365 E3.

- [ ] **Step 1: Write failing resolver/API tests.** Assert the six exact mappings in Interfaces, case-insensitive matching, null for unknown codes, unchanged counts/IDs, and search by name and part number. Test a long unknown code without replacing it with a guessed name.
- [ ] **Step 2: Run targeted .NET tests.** Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~LicenseInventoryTests` and `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~LicenseEndpointTests`; expect the new assertions to fail.
- [ ] **Step 3: Implement the resolver and API mapping.** Use a versioned in-repo dictionary with source/update notes; resolve from `skuPartNumber` in `GraphLicenseOverviewReader`. Do not add a Graph request or change the response contract.
- [ ] **Step 4: Write failing UI tests.** Assert friendly primary label, secondary code, accessible roster/export actions, compact summary layout, and stable unavailable state with a reported issue.
- [ ] **Step 5: Run the UI tests to see the failure.** Run `npm --prefix src/Web run test:behavior -- LicensesPage.test.tsx`; expect the new assertions to fail.
- [ ] **Step 6: Adapt Licenses.** Use Tasks 2 and 5 primitives while preserving roster paging, exports, and SKU identifiers.
- [ ] **Step 7: Re-run targeted API and UI tests.** Run the two .NET commands from Step 2 and the web command from Step 5; expect pass.
- [ ] **Step 8: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 9: Commit the license task.** Commit `feat: show friendly Microsoft license names`.

### Task 7: Align remaining modules and complete visual verification

**Files:** Modify `src/Web/src/features/exchange/ExchangeOverviewPage.tsx`, `src/Web/src/features/audit/AuditActivityPage.tsx`, `src/Web/src/features/overview/OverviewPage.tsx`, `src/Web/src/features/users/UserDetailPage.tsx`, `src/Web/src/styles/theme.css`; test their existing matching files under `tests/Web.UnitTests/features/` and add focused cases there.

**Interfaces:** Consume `WorkspacePageHeader`, `ResponsiveDataView`, `WorkspaceDataState`, and `useWorkspaceIssueReporter`. Preserve Exchange's directory-backed, unverified-inventory wording and on-demand mailbox verification. Preserve overview's distinction between unavailable and zero counts.

- [ ] **Step 1: Write failing page tests.** Assert consistent action/filters/results order, compact row summaries, partial-data labels, Exchange 403/missing-mailbox row isolation, activity long-reference wrapping, overview metrics staying unavailable rather than zero, and issue clearing after a successful retry.
- [ ] **Step 2: Run focused tests.** Run `npm --prefix src/Web run test:behavior -- ExchangeOverviewPage.test.tsx AuditActivityPage.test.tsx OverviewPage.test.tsx UserDetailPage.test.tsx`; expect new assertions to fail.
- [ ] **Step 3: Adapt remaining pages.** Use shared page anatomy, move verbose Graph diagnostics into Notifications, keep local retry/empty text brief, and preserve all existing feature and safety actions.
- [ ] **Step 4: Re-run focused page tests.** Run the focused command above; expect pass.
- [ ] **Step 5: Run all web behavior tests.** Run `npm --prefix src/Web run test:behavior`; expect pass.
- [ ] **Step 6: Build the web app.** Run `npm --prefix src/Web run build`; expect pass.
- [ ] **Step 7: Run .NET tests.** Run `dotnet test Atea.UnifiedWorkplace.sln`; expect pass or a clearly isolated environment prerequisite.
- [ ] **Step 8: Validate Compose configuration.** Run `docker compose config`; expect pass.
- [ ] **Step 9: Run the Web E2E suite.** Run `npm --prefix tests/Web.E2E test`; fix new failures and identify any confirmed baseline failure separately.
- [ ] **Step 10: Inspect the running app visually.** Check desktop, 375px/320px width, 200% zoom, keyboard focus, long values, and light/dark themes with a signed-in workspace admin and reader. Request the user's login/unlock assistance only if the browser session requires it. Record any tenant-side or browser limitation.
- [ ] **Step 11: Commit the final page task.** Commit `feat: align workspace operational pages` after verification.

## Completion and review

Request a fresh whole-branch code review, resolve actionable findings, and rerun affected tests. Compare the result against the spec and confirm the existing main checkout's user-owned untracked files are untouched. Hand off the reviewed branch for integration according to the user's chosen execution workflow.
