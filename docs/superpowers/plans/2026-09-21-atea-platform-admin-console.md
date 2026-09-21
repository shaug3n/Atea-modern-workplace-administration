# Atea Platform Admin Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Atea-only workspace onboarding console with development-only local login, while preserving customer Entra authentication, tenant isolation, and Microsoft Graph RBAC.

**Architecture:** The React app will have a separate `/admin` route tree and shell. A development-only cookie provider will authenticate configured Atea platform administrators, while the existing Entra bearer flow remains the only customer-facing authentication path. The admin UI will call the existing `/api/platform/*` mutation endpoints plus new read/session endpoints; future Atea Entra federation will replace only the local provider and keep the UI/API authorization boundary.

**Tech Stack:** ASP.NET Core 9, Microsoft Identity Web/JWT bearer authentication, development-only ASP.NET Core cookie authentication, PostgreSQL/EF Core, React + TypeScript + Vite, Vitest/React Testing Library, xUnit/FluentAssertions, Playwright, existing Atea tokens and Inter font assets.

**Spec:** `docs/superpowers/specs/2026-09-21-atea-platform-admin-design.md`

## Global Constraints

- `LocalDevelopment` authentication is enabled only when the API environment is `Development` and the explicit local-admin gate is enabled.
- The local administrator receives platform-admin claims only; it never receives Microsoft Graph permissions or customer directory authority.
- `AteaAdmin:LocalDevelopment:AllowAllWorkspaces` is accepted only in Development to make the local demo onboarding flow complete; production and future Atea Entra federation require explicit workspace scopes.
- Customer routes continue to require Entra bearer authentication, verified tenant claims, workspace membership, and effective Microsoft Graph permissions.
- Existing `IPlatformAuthorization` remains authoritative for `/api/platform/*` routes and workspace-specific scope remains enforced for membership and invitation operations.
- The API must never log or persist passwords, session values, access tokens, refresh tokens, invitation nonces, or raw authorization headers.
- Invitation instructions are returned once to the authorized operator; invitation hashes and usable plaintext nonces are never returned in workspace list/detail responses.
- Workspace and tenant identifiers are validated server-side; the browser cannot select a customer tenant for customer directory operations.
- Use the existing Atea logo assets, bundled Inter font, semantic theme tokens, light/dark mode, keyboard focus treatment, and plain English UI copy.
- Do not add a second customer RBAC system, local customer passwords, or a hidden Atea super-admin path.

## Review Focus

- **Local auth enabled outside Development:** startup must reject the configuration and tests must prove no local-admin login succeeds outside Development; covered by Task 1.
- **Local cookie reaching customer APIs:** a local admin cookie must not satisfy `/api/session`, `/api/capabilities`, or Graph-backed customer routes; covered by Task 1.
- **Cross-workspace reads or mutations:** an admin must not read or mutate a workspace outside configured platform scope; covered by Task 2.
- **Invitation secret exposure:** list/detail responses must contain status metadata only and must not contain nonce hashes or invitation URLs; covered by Task 2 and Task 4.
- **Admin/customer route confusion:** `/admin` must use the local admin session while customer routes still use Entra, including a clean sign-out and navigation boundary; covered by Task 3.

## File Map

### API

- Create `src/Api/Features/AdminAuth/LocalAdminOptions.cs` for development-only configuration and validation, including the explicit local all-workspaces gate.
- Create `src/Api/Features/AdminAuth/LocalAdminAuthentication.cs` for local credential verification, principal creation, and cookie scheme constants.
- Create `src/Api/Features/AdminAuth/AdminAuthEndpoints.cs` for login, logout, and current-session endpoints.
- Modify `src/Api/Authorization/PlatformAuthorization.cs` to register the local cookie scheme only in Development and expose the named platform-admin policy.
- Modify `src/Api/Features/Workspaces/WorkspaceProvisioningService.cs` and `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceProvisioningRepository.cs` for scoped workspace listing/detail reads.
- Modify `src/Api/Infrastructure/Persistence/Repositories/WorkspaceProvisioningRepository.cs` to load workspace, membership, and safe invitation metadata.
- Modify `src/Api/Features/Workspaces/WorkspaceContracts.cs` for admin read/detail DTOs and safe invitation/member DTOs.
- Modify `src/Api/Features/Workspaces/WorkspaceEndpoints.cs` to expose authorized platform read endpoints and apply the named platform-admin policy to the platform route group.
- Modify `src/Api/Program.cs` to register options, authentication endpoints, and the local-admin policy without changing the customer bearer path.
- Modify `src/Api/appsettings.Development.json` and `.env.example` with non-secret configuration names and local-only guidance.
- Test `tests/Api.UnitTests/Features/AdminAuth/LocalAdminAuthenticationTests.cs` for credential, claims, and environment behavior.
- Test `tests/Api.IntegrationTests/Features/AdminAuth/AdminAuthEndpointTests.cs` for cookie login/logout/session and customer-route separation.
- Test `tests/Api.UnitTests/Workspaces/WorkspaceProvisioningServiceTests.cs` or a focused new test for list/detail projection and secret omission.
- Test `tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs` for platform list/detail authorization, scope, duplicate handling, and safe DTOs.

### Web

- Create `src/Web/src/features/admin/AdminApp.tsx` for the admin route tree and session loading boundary.
- Create `src/Web/src/features/admin/adminAuthApi.ts` for same-origin cookie login/logout/session calls.
- Create `src/Web/src/features/admin/AdminShell.tsx` for the Atea-branded platform console shell.
- Create `src/Web/src/features/admin/AdminLoginPage.tsx` for local development login.
- Create `src/Web/src/features/admin/WorkspaceListPage.tsx` for workspace listing and creation entry point.
- Create `src/Web/src/features/admin/WorkspaceCreateForm.tsx` for tenant-ID/display-name validation and create handling.
- Create `src/Web/src/features/admin/WorkspaceDetailPage.tsx` for memberships, invitation creation, connection status, and safe handoff output.
- Create `src/Web/src/features/admin/adminApi.ts` for typed platform workspace calls and response models.
- Modify `src/Web/src/main.tsx` to render the admin route tree independently from the customer MSAL provider.
- Modify `src/Web/src/messages/en.ts` for admin copy and actionable error states.
- Modify `src/Web/src/styles/theme.css` only for scoped admin layout/control styles that are not covered by existing tokens.
- Test `tests/Web.UnitTests/features/admin/AdminApp.test.tsx`, `AdminLoginPage.test.tsx`, and workspace page tests for guards, forms, loading/error/empty states, and secret omission.
- Test `tests/Web.E2E/admin-onboarding.spec.tsx` for local login, workspace creation, membership/invitation handoff, and customer/admin route separation using the repository's existing Vitest/jsdom E2E harness.

### Documentation and verification

- Modify `docs/testing/test-tenant.md` with the local admin onboarding sequence and explicit distinction between Atea platform login and customer Entra login.
- Modify `README.md` with the local admin environment variables and startup commands.
- Modify `progress.md` only if the implementation workflow records task completion there.

---

### Task 1: Development-only local Atea admin authentication

**Files:**
- Create: `src/Api/Features/AdminAuth/LocalAdminOptions.cs`
- Create: `src/Api/Features/AdminAuth/LocalAdminAuthentication.cs`
- Create: `src/Api/Features/AdminAuth/AdminAuthEndpoints.cs`
- Modify: `src/Api/Authorization/PlatformAuthorization.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Modify: `.env.example`
- Test: `tests/Api.UnitTests/Features/AdminAuth/LocalAdminAuthenticationTests.cs`
- Test: `tests/Api.IntegrationTests/Features/AdminAuth/AdminAuthEndpointTests.cs`

**Interfaces:**
- `LocalAdminOptions`: `Enabled`, `Username`, `Password`, `ObjectId`, `DisplayName`, and `AllowAllWorkspaces`, bound from `AteaAdmin:LocalDevelopment`.
- `LocalAdminAuthentication`: `const string Scheme = "LocalAteaAdmin"`; `ClaimsPrincipal CreatePrincipal(LocalAdminOptions options)`; `bool IsConfigured(IHostEnvironment environment, LocalAdminOptions options)`.
- `AdminAuthEndpoints`: `POST /api/admin-auth/login` accepts `{ username, password }`; `POST /api/admin-auth/logout`; `GET /api/admin-auth/session` returns `{ authenticated, displayName, objectId }`.
- `PlatformAuthorization.AddPlatformAuthorization` registers the cookie scheme only when `environment.IsDevelopment()` and the explicit gate is enabled; the named policy `PlatformAdminPolicy` accepts the local cookie scheme in Development and remains separate from the default bearer policy. `CanManageWorkspace` may use `AllowAllWorkspaces` only for the local principal in Development.

- [ ] **Step 1: Write the failing unit tests for configuration and principals.**

  Add tests that prove: correct credentials create a principal containing `oid`, `name`, and a platform-admin role claim; wrong credentials fail; missing username/password/object ID disables configuration; `AllowAllWorkspaces` is ignored outside Development; and `IsConfigured` returns false when the environment is not Development even if the gate is true.

- [ ] **Step 2: Run the focused unit tests and verify the expected failures.**

  Run:

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~LocalAdminAuthenticationTests
  ```

  Expected result: the new tests fail because the local-admin types and principal factory do not exist.

- [ ] **Step 3: Implement the minimal local-admin options and principal factory.**

  Bind `AteaAdmin:LocalDevelopment`, validate the configured GUID object ID, and create a principal whose `oid` matches the configured platform allowlist identity. Do not include customer tenant claims that could make the principal eligible for customer workspace resolution.

- [ ] **Step 4: Write the failing integration tests for login, logout, and route separation.**

  Add a `WebApplicationFactory<Program>` configured for Development and local credentials. Assert that valid login returns `200` and a cookie, `GET /api/admin-auth/session` returns the local display name, logout clears the session, invalid credentials return a generic `401`, `/api/platform/workspaces` can use the local policy, and `/api/session` does not accept the local cookie.

- [ ] **Step 5: Run the focused integration tests and verify the expected failures.**

  Run:

  ```bash
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~AdminAuthEndpointTests
  ```

  Expected result: the tests fail because the endpoints and cookie policy are not registered.

- [ ] **Step 6: Implement cookie authentication and admin-auth endpoints.**

  Use an encrypted `HttpOnly` cookie with `SameSite=Strict`; use `SecurePolicy=SameAsRequest` for local HTTP development; return generic authentication failures; never log the submitted password. Register the named platform policy and leave the default Entra bearer scheme unchanged for customer routes.

- [ ] **Step 7: Run the focused API tests and the existing authorization suite.**

  Run:

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~LocalAdminAuthenticationTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~AdminAuthEndpointTests|FullyQualifiedName~AuthenticationTests
  ```

  Expected result: all focused tests pass and existing customer authentication tests remain green.

- [ ] **Step 8: Commit the isolated authentication slice.**

  ```bash
  git add src/Api/Features/AdminAuth src/Api/Authorization/PlatformAuthorization.cs src/Api/Program.cs src/Api/appsettings.Development.json .env.example tests/Api.UnitTests/Features/AdminAuth tests/Api.IntegrationTests/Features/AdminAuth
  git commit -m "feat: add development-only Atea admin authentication"
  ```

### Task 2: Platform workspace read API and safe admin projections

**Files:**
- Modify: `src/Api/Features/Workspaces/WorkspaceContracts.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceProvisioningService.cs`
- Modify: `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceProvisioningRepository.cs`
- Modify: `src/Api/Infrastructure/Persistence/Repositories/WorkspaceProvisioningRepository.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`
- Test: `tests/Api.UnitTests/Workspaces/WorkspaceProvisioningServiceTests.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs`

**Interfaces:**
- `PlatformWorkspaceScope` is a shared record with `bool IsAll` and `IReadOnlySet<Guid> WorkspaceIds`.
- `IPlatformAuthorization.GetWorkspaceScope(ClaimsPrincipal)` returns the configured workspace scope, or `PlatformWorkspaceScope(IsAll: true, ...)` only for the Development-only local all-workspaces gate.
- `IWorkspaceProvisioningRepository.ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken)` returns `IReadOnlyList<Workspace>` with memberships loaded but no invitation nonce/hash fields.
- `IWorkspaceProvisioningRepository.GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken)` returns a safe detail projection or null.
- `WorkspaceProvisioningService.ListAsync(...)` and `GetAdminDetailAsync(...)` expose the repository reads without weakening scope.
- `WorkspaceAdminDetailDto` contains workspace metadata, connection state, `WorkspaceMembershipDto[]`, and `InvitationSummaryDto[]`; `InvitationSummaryDto` contains only ID, email, display name, expiry, and redemption timestamp.

- [ ] **Step 1: Write the failing service tests for safe list/detail projections.**

  Test that an in-scope workspace is returned, an out-of-scope workspace is omitted, memberships are included, invitation status metadata is included, and `NonceHash`/invitation URLs are absent from the DTO contract.

- [ ] **Step 2: Run the focused service tests and verify they fail for missing interfaces/DTOs.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~WorkspaceProvisioningServiceTests
  ```

- [ ] **Step 3: Implement scoped repository queries and safe DTO mapping.**

  Use `AsNoTracking`, restrict by the supplied workspace scope, load memberships and invitation status fields, and never serialize the persistence entities directly. Return an empty list for an empty authorized scope.

- [ ] **Step 4: Write the failing endpoint tests for platform list/detail and scope.**

  Assert `GET /api/platform/workspaces` returns only authorized workspaces, the local Development all-workspaces gate can list the newly created workspace, `GET /api/platform/workspaces/{id}` returns `404` outside scope, and responses do not contain nonce hashes or invitation URL fields.

- [ ] **Step 5: Implement the named platform policy on the platform route group and add the read endpoints.**

  Keep existing create/membership/invitation mutations unchanged except for the policy name. Resolve the caller's workspace scope through `GetWorkspaceScope`, pass that scope to the repository read methods, and continue using `IPlatformAuthorization.CanManageWorkspace` for workspace-specific operations.

- [ ] **Step 6: Run workspace API tests and the complete API unit/integration suites.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~WorkspaceProvisioningServiceTests|FullyQualifiedName~WorkspaceIsolationTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~WorkspaceEndpointsTests
  ```

- [ ] **Step 7: Commit the platform read slice.**

  ```bash
  git add src/Api/Features/Workspaces src/Api/Infrastructure/Persistence/Repositories tests/Api.UnitTests/Workspaces/WorkspaceProvisioningServiceTests.cs tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs
  git commit -m "feat: expose scoped workspace admin reads"
  ```

### Task 3: Atea admin frontend authentication boundary and shell

**Files:**
- Create: `src/Web/src/features/admin/AdminApp.tsx`
- Create: `src/Web/src/features/admin/adminAuthApi.ts`
- Create: `src/Web/src/features/admin/AdminShell.tsx`
- Create: `src/Web/src/features/admin/AdminLoginPage.tsx`
- Modify: `src/Web/src/main.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/styles/theme.css`
- Test: `tests/Web.UnitTests/features/admin/AdminApp.test.tsx`
- Test: `tests/Web.UnitTests/features/admin/AdminLoginPage.test.tsx`

**Interfaces:**
- `adminAuthApi.login(username: string, password: string): Promise<AdminSession>` uses `credentials: 'include'` and returns only safe session metadata.
- `adminAuthApi.getSession(): Promise<AdminSession | null>` treats `401` as signed out.
- `AdminApp` owns local admin session state and renders login or protected admin routes.
- Customer `AuthProvider` continues to wrap only the existing customer app; `/admin` is selected in `main.tsx` before MSAL initialization. The customer branch is lazy-loaded so missing customer Entra variables cannot break the local admin entry point.

- [ ] **Step 1: Write failing frontend tests for the route/auth boundary.**

  Test that `/admin` renders the local login without constructing the MSAL customer provider, a valid session renders the admin shell, a `401` renders the login page, and customer paths still render the existing customer authentication flow.

- [ ] **Step 2: Run the focused frontend tests and verify they fail.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/admin/AdminApp.test.tsx tests/Web.UnitTests/features/admin/AdminLoginPage.test.tsx
  ```

- [ ] **Step 3: Implement the cookie-based admin API client and route boundary.**

  Use same-origin `/api/admin-auth/*` requests with `credentials: 'include'`, generic error copy, and full-page navigation when switching between `/admin` and customer routes so each auth provider initializes cleanly.

- [ ] **Step 4: Implement the Atea admin shell and login page.**

  Reuse the bundled grey/white Atea logos and theme provider. Provide labelled username/password controls, visible focus, disabled submit state, generic invalid-login error, sign-out action, and an explicit notice that this login is for local development only.

- [ ] **Step 5: Run focused tests and the full web unit suite.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/admin/AdminApp.test.tsx tests/Web.UnitTests/features/admin/AdminLoginPage.test.tsx
  npm run test --prefix tests/Web.UnitTests
  ```

  Expected result: new tests and all existing web tests pass.

- [ ] **Step 6: Commit the admin auth/shell slice.**

  ```bash
  git add src/Web/src/features/admin src/Web/src/main.tsx src/Web/src/messages/en.ts src/Web/src/styles/theme.css tests/Web.UnitTests/features/admin
  git commit -m "feat: add Atea admin login shell"
  ```

### Task 4: Workspace list, creation, detail, and invitation handoff UI

**Files:**
- Create: `src/Web/src/features/admin/adminApi.ts`
- Create: `src/Web/src/features/admin/WorkspaceListPage.tsx`
- Create: `src/Web/src/features/admin/WorkspaceCreateForm.tsx`
- Create: `src/Web/src/features/admin/WorkspaceDetailPage.tsx`
- Modify: `src/Web/src/features/admin/AdminApp.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/styles/theme.css`
- Test: `tests/Web.UnitTests/features/admin/WorkspaceListPage.test.tsx`
- Test: `tests/Web.UnitTests/features/admin/WorkspaceCreateForm.test.tsx`
- Test: `tests/Web.UnitTests/features/admin/WorkspaceDetailPage.test.tsx`

**Interfaces:**
- `adminApi.listWorkspaces(): Promise<WorkspaceSummary[]>`
- `adminApi.createWorkspace(input: { tenantId: string; displayName: string }): Promise<WorkspaceSummary>`
- `adminApi.getWorkspace(workspaceId: string): Promise<WorkspaceAdminDetail>`
- `adminApi.addMembership(workspaceId: string, input: { tenantObjectId: string; email: string; platformRole: string; isAteaOperator: boolean }): Promise<Membership>`
- `adminApi.createInvitation(workspaceId: string, input: { email: string; displayName: string; expiresAt: string; approvedTenantObjectId?: string }): Promise<{ invitationUrl: string; expiresAt: string }>`

- [ ] **Step 1: Write failing page tests for loading, empty, validation, and API errors.**

  Test workspace list loading/empty/error states; reject malformed tenant GUIDs and blank display names; render a created workspace; show membership and invitation status; display the one-time invitation URL only in the creation result and never display nonce/hash fields.

- [ ] **Step 2: Run the focused page tests and verify they fail.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/admin/WorkspaceListPage.test.tsx tests/Web.UnitTests/features/admin/WorkspaceCreateForm.test.tsx tests/Web.UnitTests/features/admin/WorkspaceDetailPage.test.tsx
  ```

- [ ] **Step 3: Implement typed admin API calls and response guards.**

  Set `Content-Type` only for JSON requests, use `credentials: 'include'`, translate `401`, `403`, `409`, and `503` into actionable English messages, and reject unexpected response shapes before rendering.

- [ ] **Step 4: Implement workspace list and creation flow.**

  Add a clear primary “Create workspace” action, tenant ID validation, display-name validation, duplicate conflict handling, and navigation to the created workspace detail page.

- [ ] **Step 5: Implement workspace detail, memberships, and invitation handoff.**

  Add member fields for Entra object ID, email, platform role, and Atea-operator flag. Add invitation fields for email, display name, expiry, and optional approved tenant object ID. Render safe invitation metadata and a copy button for the one-time URL with a warning that the URL is a credential-like secret.

- [ ] **Step 6: Run focused tests and the full frontend test/build suites.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/admin/WorkspaceListPage.test.tsx tests/Web.UnitTests/features/admin/WorkspaceCreateForm.test.tsx tests/Web.UnitTests/features/admin/WorkspaceDetailPage.test.tsx
  npm run test --prefix tests/Web.UnitTests
  npm run build --prefix src/Web
  ```

- [ ] **Step 7: Commit the workspace-management UI slice.**

  ```bash
  git add src/Web/src/features/admin src/Web/src/messages/en.ts src/Web/src/styles/theme.css tests/Web.UnitTests/features/admin
  git commit -m "feat: add Atea workspace onboarding console"
  ```

### Task 5: Local runbook and end-to-end onboarding verification

**Files:**
- Modify: `README.md`
- Modify: `docs/testing/test-tenant.md`
- Create: `tests/Web.E2E/admin-onboarding.spec.tsx`
- Modify: `tests/Web.E2E/vitest.config.ts` only if the existing jsdom config needs an admin-specific fixture alias

**Interfaces:**
- The documented local configuration uses `AteaAdmin__LocalDevelopment__Enabled`, `AteaAdmin__LocalDevelopment__Username`, `AteaAdmin__LocalDevelopment__Password`, `AteaAdmin__LocalDevelopment__ObjectId`, `AteaAdmin__LocalDevelopment__DisplayName`, and `AteaAdmin__LocalDevelopment__AllowAllWorkspaces`.
- The local runbook starts PostgreSQL with `docker compose up -d postgres`, starts the API on port 8080, starts Vite on port 5173, and uses `/admin` for platform onboarding.

- [ ] **Step 1: Update the local runbook with the exact configuration and startup commands.**

  Document all six local-admin keys, the Development-only warning, the local all-workspaces limitation, `docker compose up -d postgres`, the API command, the Vite command, and the distinction between `/admin` local login and customer Entra login. Verify the names with `rg -n 'AteaAdmin__LocalDevelopment__|docker compose up -d postgres|/admin' README.md docs/testing/test-tenant.md`.

- [ ] **Step 2: Write the failing Vitest/jsdom E2E flow.**

  The flow must render `/admin`, log in through the cookie-auth API fixture, create a workspace for the demo tenant, open its detail page, add a nominated customer administrator, create an invitation, verify the safe status table, and assert that the customer route still presents the Entra sign-in boundary. Keep the existing E2E harness deterministic and do not claim this test is a live browser or real-tenant proof.

- [ ] **Step 3: Run the E2E test before implementation wiring is complete and record the expected failure.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.E2E/admin-onboarding.spec.tsx
  ```

- [ ] **Step 4: Implement the deterministic API fixture setup without bypassing the production API contracts.**

  Use the real local PostgreSQL migrations and API endpoints. Do not insert workspace rows directly from the browser test and do not place invitation nonces or access tokens in test output.

- [ ] **Step 5: Run the complete verification set from the worktree.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj
  npm run test --prefix tests/Web.UnitTests
  npm run test --prefix tests/Web.E2E
  npm run test:behavior --prefix src/Web -- --run
  npm run build --prefix src/Web
  docker compose config --quiet
  ```

  Expected result: all existing tests and the new admin flow pass; the only skipped tests must be explicitly documented by the existing test harness.

- [ ] **Step 6: Commit the runbook and end-to-end verification slice.**

  ```bash
  git add README.md docs/testing/test-tenant.md tests/Web.E2E/admin-onboarding.spec.tsx tests/Web.E2E/vitest.config.ts
  git commit -m "test: verify local Atea workspace onboarding"
  ```

### Task 6: Whole-branch review and security verification

**Files:**
- Review all files changed by Tasks 1–5.
- Modify only where review findings require a focused correction; add a regression test before each correction.

- [ ] **Step 1: Review the diff against the approved specification.**

  Confirm the implementation has a separate admin route tree, a Development-only local provider, unchanged customer Entra behavior, scoped platform endpoints, safe invitation responses, and Atea light/dark accessibility treatment.

- [ ] **Step 2: Run repository security and contract checks.**

  ```bash
  python3 infra/tests/validate_contract.py
  git diff --check HEAD~6..HEAD
  rg -n 'access_token|refresh_token|Authorization:|NonceHash|Password' src/Api src/Web tests --glob '!**/bin/**' --glob '!**/obj/**'
  ```

  Review matches manually and confirm no secret-bearing value is logged or rendered outside the intended local password input and one-time invitation result.

- [ ] **Step 3: Run the production configuration guard.**

  Start the API with `ASPNETCORE_ENVIRONMENT=Production` and `AteaAdmin__LocalDevelopment__Enabled=true` in a disposable local process. Verify startup fails with a clear configuration error and no local login endpoint is usable.

- [ ] **Step 4: Perform final local manual verification.**

  Sign in to `/admin`, create the demo workspace, add the nominated demo user, create the invitation, open the invitation in a separate customer Entra session, redeem it, and confirm the existing customer app resolves the workspace only after membership exists.

- [ ] **Step 5: Commit any final verified correction and report remaining Azure federation work.**

  The final report must distinguish completed local admin functionality from the deferred Atea Entra federation and must include the exact verification commands and results.
