# Atea Unified Workplace Local V1 Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the existing Atea Unified Workplace implementation so the full V1 customer journey, including invitation redemption, consent/connection handling, permission-aware administration and PIM handoff, can be demonstrated locally against the real M365 test tenant. Azure deployment is out of scope.

**Architecture:** Preserve the current split route tree: Development-only local Atea platform administration at `/admin`, and Entra-authenticated customer routes for `/invitations/{nonce}` and the workspace application. The API remains the only Microsoft Graph caller, PostgreSQL stores platform metadata and safe audit/idempotency state, and capability evaluation remains the enforcement point for tenant RBAC, delegated scopes, and PIM states.

**Tech Stack:** ASP.NET Core 9, Microsoft.Identity.Web/JWT bearer, EF Core/Npgsql/PostgreSQL, React + TypeScript + Vite, MSAL browser, Vitest + React Testing Library + jsdom, xUnit + FluentAssertions, Docker Compose and the existing real-tenant opt-in scenario harness.

**Spec:** `docs/superpowers/specs/2026-09-21-atea-local-v1-completion-design.md`, grounded in `docs/superpowers/specs/2026-09-17-atea-unified-workplace-design.md`.

## Global Constraints

- The local Atea provider is accepted only when the API environment is `Development`; it never grants customer Graph authority.
- Customer invitation, consent, directory and PIM routes use Entra bearer authentication and server-resolved tenant/workspace membership.
- `Onboarding__PublicBaseUrl=http://localhost:5173` is required for the local demo; no generated link may fall back to `https://workplace.example`.
- The API never returns or logs access tokens, refresh tokens, client secrets, raw authorization headers, invitation nonce hashes or plaintext invitation nonces after the one-time creation response.
- Microsoft Graph remains the source of truth for directory data and effective user authority; PostgreSQL stores only platform metadata, connection state, safe audit events and idempotency records.
- Every mutating UI action has API capability enforcement, explicit confirmation where required, an idempotency key, pending/success/error states and a refresh path.
- V1 is English-only, uses the existing Atea logo/Inter assets and semantic light/dark tokens, and does not add Azure deployment work.
- Real-tenant checks are opt-in and use the existing dedicated test tenant; deterministic unit/integration/browser-style tests must remain runnable without tenant credentials.

## Review Focus

- **Placeholder or unroutable invitation URLs:** local configuration and frontend route tests must prove a generated invitation opens `/invitations/{nonce}` on the configured origin.
- **Invitation misuse:** API tests must prove wrong tenant, wrong user, expired and reused invitations fail closed and do not create membership.
- **Consent callback ambiguity:** tests must prove state is signed, tenant/workspace-bound, single-purpose and mapped to a truthful connection check rather than a raw OAuth error.
- **Visible but nonfunctional controls:** UI tests must exercise create/edit/reactivate, group, license and PIM actions through their actual API calls and refresh states.
- **Permission/RBAC drift:** tests must prove Global Reader read-only behavior, User Administrator mutations, missing delegated consent and eligible-inactive PIM handoff/activation states.

## Current Baseline and File Map

The implementation starts from branch `codex/atea-unified-workplace-mvp`, which already contains the platform admin console, workspace persistence, Graph adapters, capability evaluator, user/license/group/PIM services, overview, audit and settings screens. The tasks below complete and wire those pieces; they do not recreate them.

Primary new or modified areas:

- `src/Api/Features/Workspaces/InvitationContracts.cs`, `WorkspaceEndpoints.cs`, `ConsentChallengeService.cs` and onboarding configuration for public URL, redemption and consent completion.
- `src/Web/src/features/invitations/`, `src/Web/src/features/workspace-settings/ConsentCallbackPage.tsx`, customer route selection in `main.tsx`/`App.tsx`, and typed API clients.
- `src/Web/src/features/users/`, `src/Web/src/features/licenses/`, `src/Web/src/features/pim/` for missing action wiring and refresh/error behavior.
- `src/Api/Features/Users/`, `Groups/`, `Licenses/`, `Pim/`, and `Infrastructure/Graph/` only where the existing API contract cannot support the complete V1 UI.
- `tests/Api.UnitTests`, `tests/Api.IntegrationTests`, `tests/Web.UnitTests`, `tests/Web.E2E`, `tests/RealTenant`, `README.md` and `docs/testing/test-tenant.md`.

### Task 1: Make local onboarding URLs and configuration explicit

**Files:**
- Create: `src/Api/Features/Workspaces/OnboardingOptions.cs`
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/appsettings.Development.json`
- Modify: `.env.example`
- Modify: `README.md`
- Modify: `docs/testing/test-tenant.md`
- Test: `tests/Api.UnitTests/Workspaces/OnboardingOptionsTests.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/InvitationConfigurationTests.cs`

**Interfaces:**
- `OnboardingOptions` binds `Onboarding:PublicBaseUrl`, `ConsentRedirectUri`, and `ConsentSigningKey`.
- `OnboardingOptions.Validate(IHostEnvironment)` rejects an empty/placeholder URL, an invalid absolute URI, a non-local HTTP URL outside Development, and a missing consent key when consent start is enabled.
- `InvitationService` receives the validated `Uri PublicBaseUri`; it must not contain a fallback placeholder.

- [ ] **Step 1: Write failing configuration tests.**

  Add tests for `http://localhost:5173` being accepted in Development, `https://workplace.example` being rejected, an invalid/relative URL being rejected, and HTTP being rejected outside Development.

- [ ] **Step 2: Run the focused tests and verify the expected failure.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~OnboardingOptionsTests
  ```

  Expected result: compile or assertion failures because `OnboardingOptions` and startup validation do not exist.

- [ ] **Step 3: Implement binding and startup validation.**

  Register `IOptions<OnboardingOptions>`, validate it before `InvitationService` is constructed, and pass `new Uri(options.PublicBaseUrl, UriKind.Absolute)` to the service. Preserve `ConsentSigningKey` as a secret value and do not print it.

- [ ] **Step 4: Add local environment documentation and integration coverage.**

  Add these exact local values to `.env.example` and the runbook:

  ```text
  Onboarding__PublicBaseUrl=http://localhost:5173
  Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
  Onboarding__ConsentSigningKey=<base64-encoded-32-byte-development-key>
  ```

  Add an integration test that creates an invitation with `Onboarding__PublicBaseUrl=http://localhost:5173` and asserts its URL starts with `http://localhost:5173/invitations/`.

- [ ] **Step 5: Run focused API tests and commit.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~OnboardingOptionsTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~InvitationConfigurationTests
  git add src/Api/Features/Workspaces/OnboardingOptions.cs src/Api/Program.cs src/Api/appsettings.Development.json .env.example README.md docs/testing/test-tenant.md tests/Api.UnitTests/Workspaces/OnboardingOptionsTests.cs tests/Api.IntegrationTests/Workspaces/InvitationConfigurationTests.cs
  git commit -m "fix: make local onboarding configuration explicit"
  ```

### Task 2: Implement the customer invitation redemption journey

**Files:**
- Create: `src/Api/Features/Workspaces/InvitationContracts.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`
- Modify: `src/Api/Features/Workspaces/InvitationService.cs`
- Create: `src/Web/src/features/invitations/InvitationRedemptionPage.tsx`
- Create: `src/Web/src/features/invitations/invitationApi.ts`
- Modify: `src/Web/src/main.tsx`
- Modify: `src/Web/src/app/App.tsx`
- Modify: `src/Web/src/app/routes.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/styles/theme.css`
- Test: `tests/Api.UnitTests/Workspaces/InvitationServiceTests.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/InvitationRedemptionEndpointTests.cs`
- Test: `tests/Web.UnitTests/features/invitations/InvitationRedemptionPage.test.tsx`
- Test: `tests/Web.E2E/invitation-redemption.spec.tsx`

**Interfaces:**
- `POST /api/invitations/{nonce}/redeem` accepts the authenticated customer bearer and returns `InvitationRedemptionResponse { status, workspaceId, workspaceName, nextStep }` without returning the nonce or token.
- `InvitationRedemptionPage` receives `nonce` from the URL and calls `redeemInvitation(nonce)`. It shows signed-in account, tenant match/error, redeemed state, and a button to navigate to `/overview`.
- `isInvitationPath(pathname)` matches exactly `/invitations/{non-empty-single-segment}` and never treats `/admin` or arbitrary paths as invitations.

- [ ] **Step 1: Write failing API tests for the redemption contract.**

  Cover successful redemption without prior workspace membership, wrong tenant, wrong object/email, expired token, reused token and malformed nonce. Assert only safe fields are returned and that the second redemption does not create a duplicate membership.

- [ ] **Step 2: Run the API tests and verify the expected failures.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~InvitationServiceTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~InvitationRedemptionEndpointTests
  ```

- [ ] **Step 3: Implement the safe response and atomic redemption path.**

  Keep `WorkspaceContextMiddleware` excluded for `/api/invitations`, require bearer authentication, validate `tid`/`oid` and the invited identity, atomically mark the invitation redeemed, add membership and transition the workspace to `consent_required`.

- [ ] **Step 4: Write failing frontend route and state tests.**

  Assert `/invitations/{nonce}` renders the customer auth branch, shows a redeem action after Entra authentication, handles invalid/expired/reused responses, and navigates to `/overview` after success. Assert the nonce is never rendered in a response message or copied to logs/state beyond the route request.

- [ ] **Step 5: Implement the customer invitation page and route branch.**

  Keep `/admin` on the local cookie branch. Load the existing customer MSAL provider for invitation paths, call the same-origin API client, and use `history.pushState` to reach the customer app after redemption. Do not add a local customer password or a second auth provider.

- [ ] **Step 6: Run web tests/build and commit.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/invitations/InvitationRedemptionPage.test.tsx
  npm run test --prefix tests/Web.E2E -- --run invitation-redemption.spec.tsx
  npm run build --prefix src/Web
  git add src/Api/Features/Workspaces src/Web/src/features/invitations src/Web/src/main.tsx src/Web/src/app/App.tsx src/Web/src/app/routes.tsx src/Web/src/messages/en.ts src/Web/src/styles/theme.css tests/Api.UnitTests/Workspaces/InvitationServiceTests.cs tests/Api.IntegrationTests/Workspaces/InvitationRedemptionEndpointTests.cs tests/Web.UnitTests/features/invitations tests/Web.E2E/invitation-redemption.spec.tsx
  git commit -m "feat: complete customer invitation redemption"
  ```

### Task 3: Complete delegated consent and connection callback UX

**Files:**
- Modify: `src/Api/Features/Workspaces/ConsentChallengeService.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceContracts.cs`
- Create: `src/Web/src/features/workspace-settings/ConsentCallbackPage.tsx`
- Modify: `src/Web/src/features/overview/OverviewPage.tsx`
- Modify: `src/Web/src/app/routes.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Test: `tests/Api.UnitTests/Workspaces/ConsentChallengeTests.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/ConsentCallbackEndpointTests.cs`
- Test: `tests/Web.UnitTests/features/workspace-settings/ConsentCallbackPage.test.tsx`
- Test: `tests/Web.E2E/consent-callback.spec.tsx`

**Interfaces:**
- `POST /api/workspaces/current/consent/complete` accepts `{ state, tenant, errorCode?, errorDescription? }` and returns `{ valid, status, correlationId }`. It validates the signed state against the current workspace and tenant and never echoes raw authorization codes or error descriptions containing secrets.
- `POST /api/workspaces/current/consent/start` creates a Microsoft Entra admin-consent URL with the configured redirect URI and signed state. The redirect target is the frontend callback route, not an unimplemented `/onboarding` page.
- `ConsentCallbackPage` reads query parameters, calls the safe completion endpoint, then calls connection health check and presents the resulting state.

- [ ] **Step 1: Add failing state-validation tests.**

  Cover valid state, tampered state, wrong tenant, expired state, callback error and replay. Assert the API does not transition a workspace to `connected` solely because a callback was received; only a delegated Graph connection check may establish `connected`.

- [ ] **Step 2: Implement the signed callback contract.**

  Add a challenge validation overload that derives the signed correlation data from the state, build the `adminconsent` URL with `redirect_uri`, `state` and requested delegated scopes, and return safe correlation metadata.

- [ ] **Step 3: Add failing frontend callback tests, then implement the route.**

  Cover success, consent denial, invalid state, connection-check failure and retry. Use plain user-facing explanations and links rather than raw Graph or OAuth payloads.

- [ ] **Step 4: Run focused tests/build and commit.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~ConsentChallengeTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~ConsentCallbackEndpointTests
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/workspace-settings/ConsentCallbackPage.test.tsx
  npm run build --prefix src/Web
  git add src/Api/Features/Workspaces src/Web/src/features/workspace-settings/ConsentCallbackPage.tsx src/Web/src/features/overview/OverviewPage.tsx src/Web/src/app/routes.tsx src/Web/src/messages/en.ts tests/Api.UnitTests/Workspaces/ConsentChallengeTests.cs tests/Api.IntegrationTests/Workspaces/ConsentCallbackEndpointTests.cs tests/Web.UnitTests/features/workspace-settings/ConsentCallbackPage.test.tsx tests/Web.E2E/consent-callback.spec.tsx
  git commit -m "feat: complete delegated consent callback"
  ```

### Task 4: Wire the complete user lifecycle experience

**Files:**
- Modify: `src/Web/src/features/users/UsersPage.tsx`
- Modify: `src/Web/src/features/users/UserFilters.tsx`
- Modify: `src/Web/src/features/users/UserCreateDialog.tsx`
- Modify: `src/Web/src/features/users/UserEditDialog.tsx`
- Modify: `src/Web/src/features/users/UserDetailPage.tsx`
- Modify: `src/Web/src/features/users/IdentitySection.tsx`
- Modify: `src/Web/src/features/users/JobInformationSection.tsx`
- Modify: `src/Web/src/features/users/userMutationApi.ts`
- Modify: `src/Api/Features/Users/UserQueryService.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphDirectoryReader.cs`
- Modify: `src/Web/src/messages/en.ts`
- Test: `tests/Api.UnitTests/Users/UserQueryServiceTests.cs`
- Test: `tests/Api.IntegrationTests/Users/UserEndpointsTests.cs`
- Test: `tests/Web.UnitTests/features/users/UsersPage.test.tsx`
- Test: `tests/Web.UnitTests/features/users/UserDetailPage.test.tsx`
- Test: `tests/Web.UnitTests/features/users/UserMutationDialogs.test.tsx`
- Test: `tests/Web.E2E/user-lifecycle.spec.tsx`

**Interfaces:**
- `UserFilters` must send `tenantRole` and `license` through `fetchUsers` when selected; the UI must render real options or a truthful unavailable state based on API capability/data.
- `UserCreateDialog` submits `POST /api/users` with an idempotency key and displays the one-time temporary credential notice only in the immediate success state.
- `UserEditDialog`, disable, reactivate and create actions refresh the affected user/directory and map `denied`, `read_only`, `source_of_authority_read_only`, `throttled`, `conflict` and consent errors to plain English.

- [ ] **Step 1: Write failing tests for all currently unwired actions and filters.**

  Assert the create button opens a labelled form, edit/reactivate actions call the correct endpoints, successful mutations refresh, failed mutations preserve the user’s input, and `tenantRole`/`license` query values are encoded.

- [ ] **Step 2: Run the focused tests and confirm the current gaps.**

  ```bash
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/users/UsersPage.test.tsx tests/Web.UnitTests/features/users/UserDetailPage.test.tsx tests/Web.UnitTests/features/users/UserMutationDialogs.test.tsx
  ```

- [ ] **Step 3: Implement the UI wiring and query contract.**

  Reuse existing dialogs and mutation APIs, add controlled form state/validation, route capability decisions into each action, and refresh the directory/detail after success. Extend Graph directory query mapping only where the current adapter does not forward a supported filter.

- [ ] **Step 4: Add API regression coverage for filter forwarding and authorization.**

  Use the existing fake Graph transport to assert encoded filters, and assert User Administrator and Global Reader capability outcomes remain distinct.

- [ ] **Step 5: Run API/web tests/build and commit.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~UserQueryServiceTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~UserEndpointsTests
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/users
  npm run build --prefix src/Web
  git add src/Api/Features/Users src/Api/Infrastructure/Graph/GraphDirectoryReader.cs src/Web/src/features/users src/Web/src/messages/en.ts tests/Api.UnitTests/Users/UserQueryServiceTests.cs tests/Api.IntegrationTests/Users/UserEndpointsTests.cs tests/Web.UnitTests/features/users tests/Web.E2E/user-lifecycle.spec.tsx
  git commit -m "feat: complete user lifecycle experience"
  ```

### Task 5: Wire group and license management from user details

**Files:**
- Create: `src/Api/Features/Groups/GroupCatalogContracts.cs`
- Modify: `src/Api/Features/Groups/GroupEndpoints.cs`
- Modify: `src/Api/Features/Licenses/LicenseEndpoints.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphGroupMembershipService.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphLicenseService.cs`
- Modify: `src/Web/src/features/users/GroupsSection.tsx`
- Modify: `src/Web/src/features/users/LicensesSection.tsx`
- Modify: `src/Web/src/features/users/GroupMembershipDialog.tsx`
- Modify: `src/Web/src/features/users/LicenseAssignmentDialog.tsx`
- Modify: `src/Web/src/features/users/UserDetailPage.tsx`
- Modify: `src/Web/src/features/licenses/LicensesPage.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Test: `tests/Api.IntegrationTests/Groups/GroupEndpointTests.cs`
- Test: `tests/Api.IntegrationTests/Licenses/LicenseEndpointTests.cs`
- Test: `tests/Web.UnitTests/features/users/UserDetailPage.test.tsx`
- Test: `tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx`
- Test: `tests/Web.E2E/user-lifecycle.spec.tsx`

**Interfaces:**
- `GET /api/groups?search=&pageSize=` returns safe group choices under the current workspace and capability state.
- `GET /api/licenses` remains the license catalog/coverage source; its item IDs are the only values accepted by assignment/removal actions.
- Detail sections expose add/remove controls only when `groups.manage_members` or `licenses.assign` is allowed, otherwise show the existing permission state.

- [ ] **Step 1: Write failing API/UI tests.**

  Cover group catalog authorization, license SKU selection, add/remove calls with idempotency keys, stale detail refresh and read-only users receiving no mutation controls.

- [ ] **Step 2: Implement safe catalogs and UI controls.**

  Keep Graph IDs opaque and validate route/body ID matching server-side. Reuse `GroupMembershipDialog` and `LicenseAssignmentDialog`; render results with success/error status and reload the user detail.

- [ ] **Step 3: Run focused API/web/E2E tests and commit.**

  ```bash
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~GroupEndpointTests|FullyQualifiedName~LicenseEndpointTests
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/users/UserDetailPage.test.tsx tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx
  npm run test --prefix tests/Web.E2E -- --run user-lifecycle.spec.tsx
  git add src/Api/Features/Groups src/Api/Features/Licenses src/Api/Infrastructure/Graph/GraphGroupMembershipService.cs src/Api/Infrastructure/Graph/GraphLicenseService.cs src/Web/src/features/users src/Web/src/features/licenses src/Web/src/messages/en.ts tests/Api.IntegrationTests/Groups tests/Api.IntegrationTests/Licenses tests/Web.UnitTests/features/users/UserDetailPage.test.tsx tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx tests/Web.E2E/user-lifecycle.spec.tsx
  git commit -m "feat: complete group and license management"
  ```

### Task 6: Finish PIM, capability and operational-state UX against real Graph responses

**Files:**
- Modify: `src/Api/Infrastructure/Graph/GraphRoleAndPimService.cs`
- Modify: `src/Api/Features/Pim/PimService.cs`
- Modify: `src/Api/Features/Pim/PimEndpoints.cs`
- Modify: `src/Api/Authorization/CapabilityEvaluator.cs`
- Modify: `src/Api/Features/Overview/OverviewService.cs`
- Modify: `src/Api/Features/Workspaces/WorkspaceSettingsEndpoints.cs`
- Modify: `src/Api/Features/Audit/AuditEndpoints.cs`
- Modify: `src/Api/Infrastructure/Observability/AuditWriter.cs`
- Modify: `src/Web/src/features/pim/PimActivationDialog.tsx`
- Modify: `src/Web/src/features/pim/PimGuidedHandoff.tsx`
- Modify: `src/Web/src/features/users/RolesAndPimSection.tsx`
- Modify: `src/Web/src/components/PermissionState.tsx`
- Modify: `src/Web/src/features/overview/OverviewPage.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/app/App.tsx`
- Modify: `src/Web/src/features/audit/AuditActivityPage.tsx`
- Modify: `src/Web/src/features/workspace-settings/WorkspaceSettingsPage.tsx`
- Test: `tests/Api.UnitTests/Pim/PimServiceTests.cs`
- Test: `tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs`
- Test: `tests/Api.UnitTests/Overview/OverviewServiceTests.cs`
- Test: `tests/Api.UnitTests/Observability/AuditRedactionTests.cs`
- Test: `tests/Api.IntegrationTests/Pim/PimEndpointTests.cs`
- Test: `tests/Api.IntegrationTests/Audit/AuditEndpointTests.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/WorkspaceSettingsEndpointTests.cs`
- Test: `tests/Web.UnitTests/features/pim/PimActivationDialog.test.tsx`
- Test: `tests/Web.UnitTests/capabilities/PermissionState.test.tsx`
- Test: `tests/Web.UnitTests/features/overview/OverviewPage.test.tsx`
- Test: `tests/Web.UnitTests/features/audit/AuditActivityPage.test.tsx`
- Test: `tests/Web.UnitTests/features/workspace-settings/WorkspaceSettingsPage.test.tsx`
- Test: `tests/Web.E2E/pim-activation.spec.tsx`

**Interfaces:**
- `POST /api/pim/activations` returns one of `active`, `activation_pending`, `approval_required`, `mfa_required`, `policy_blocked`, `not_eligible`, `not_authorized` or `temporarily_unavailable`, with safe next-step/handoff metadata and Graph correlation IDs only.
- `CapabilityDecision` remains the single API/UI authorization contract. A role label alone never enables an action without effective delegated scope and active/eligible role state.

- [ ] **Step 1: Add regression tests for real Graph status mapping.**

  Cover successful activation, approval-required, MFA-required, justification-required, expired eligibility, insufficient scopes, 403/429/5xx Graph responses and active-role refresh. Assert raw Graph payloads and tokens are absent from responses.

- [ ] **Step 1b: Add regression tests for the operational shell.**

  Assert overview metrics are hidden or unavailable when `users.view` is hidden, connection-health check and consent actions render in the normal application route, database-backed settings persist `displayName` and `defaultFilters`, and audit reads require the audit capability. Assert audit metadata redacts `accessToken`, `refreshToken`, `clientSecret`, `token` and nested secret values.

- [ ] **Step 2: Fix adapter/service/UI mappings.**

  Ensure the Graph request uses the correct directory-role PIM activation contract, preserve safe correlation IDs, display tenant requirements, and provide an Entra handoff when activation cannot be completed in-app. In the same slice, enforce overview capability checks before metric/cache reads, wire connection-health actions into the normal overview route, persist all settings fields, add audit capability enforcement/filter/pagination behavior, and make audit write failures explicit rather than silently dropping the record.

- [ ] **Step 3: Run focused tests and commit.**

  ```bash
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~PimServiceTests|FullyQualifiedName~CapabilityEvaluatorTests
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~PimEndpointTests
  npm run test:behavior --prefix src/Web -- --run tests/Web.UnitTests/features/pim tests/Web.UnitTests/capabilities
  npm run test --prefix tests/Web.E2E -- --run pim-activation.spec.tsx
  git add src/Api/Infrastructure/Graph/GraphRoleAndPimService.cs src/Api/Features/Pim src/Api/Authorization/CapabilityEvaluator.cs src/Api/Features/Overview/OverviewService.cs src/Api/Features/Workspaces/WorkspaceSettingsEndpoints.cs src/Api/Features/Audit/AuditEndpoints.cs src/Api/Infrastructure/Observability/AuditWriter.cs src/Web/src/features/pim src/Web/src/features/users/RolesAndPimSection.tsx src/Web/src/components/PermissionState.tsx src/Web/src/features/overview/OverviewPage.tsx src/Web/src/features/audit/AuditActivityPage.tsx src/Web/src/features/workspace-settings/WorkspaceSettingsPage.tsx src/Web/src/app/App.tsx src/Web/src/messages/en.ts tests/Api.UnitTests/Pim tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs tests/Api.UnitTests/Overview/OverviewServiceTests.cs tests/Api.UnitTests/Observability/AuditRedactionTests.cs tests/Api.IntegrationTests/Pim tests/Api.IntegrationTests/Audit/AuditEndpointTests.cs tests/Api.IntegrationTests/Workspaces/WorkspaceSettingsEndpointTests.cs tests/Web.UnitTests/features/pim tests/Web.UnitTests/capabilities tests/Web.UnitTests/features/overview/OverviewPage.test.tsx tests/Web.UnitTests/features/audit/AuditActivityPage.test.tsx tests/Web.UnitTests/features/workspace-settings/WorkspaceSettingsPage.test.tsx tests/Web.E2E/pim-activation.spec.tsx
  git commit -m "feat: complete permission and PIM experience"
  ```

### Task 7: Prove the complete local product journey and update the runbook

**Files:**
- Create: `tests/Web.E2E/local-v1-journey.spec.tsx`
- Create: `tests/Api.IntegrationTests/Workspaces/LocalV1JourneyTests.cs`
- Modify: `tests/RealTenant/README.md`
- Modify: `tests/RealTenant/Scenarios/OnboardingScenario.cs`
- Modify: `tests/RealTenant/real-tenant.env.example`
- Modify: `docs/testing/test-tenant.md`
- Modify: `README.md`
- Modify: `docker-compose.yml`
- Modify: `.env.example`

**Interfaces:**
- The deterministic browser-style journey stubs only same-origin API responses and covers `/admin` login, workspace creation, membership, invitation creation, customer invitation redemption, overview connection state, user action permission states and PIM handoff.
- The opt-in real-tenant scenario requires `ATEA_REAL_TENANT_RUN=true`, `ATEA_REAL_TENANT_API_BASE_URL`, `ATEA_REAL_TENANT_ID`, `ATEA_REAL_TENANT_OBJECT_ID` and a short-lived delegated token supplied outside source control. It must never emit the token or invitation nonce.

- [ ] **Step 1: Write the failing full-journey tests.**

  Assert that the configured invitation origin is local, the customer route is reachable, the customer session is not the Atea admin cookie, and a local admin cannot call customer Graph routes.

- [ ] **Step 2: Implement deterministic fixtures and runbook commands.**

  Document the exact three-terminal startup, `Onboarding__PublicBaseUrl`, test tenant roles, PIM setup, admin consent, invitation redemption and teardown. Add a Compose health dependency so API startup waits for PostgreSQL health without changing production semantics.

- [ ] **Step 3: Run the complete local verification set.**

  ```bash
  docker compose config
  dotnet test tests/Api.UnitTests/Api.UnitTests.csproj
  dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj
  npm run test:behavior --prefix src/Web -- --run
  npm run test --prefix tests/Web.UnitTests
  npm run test --prefix tests/Web.E2E
  npm run build --prefix src/Web
  ```

- [ ] **Step 4: Commit the local V1 verification slice.**

  ```bash
  git add tests/Web.E2E/local-v1-journey.spec.tsx tests/Api.IntegrationTests/Workspaces/LocalV1JourneyTests.cs tests/RealTenant docs/testing/test-tenant.md README.md docker-compose.yml .env.example
  git commit -m "test: prove complete local v1 journey"
  ```

### Task 8: Whole-branch security and completion review

**Files:**
- Review only: all changed files from Tasks 1–7
- Update if required: `docs/security/data-isolation.md`, `docs/security/graph-permission-matrix.md`, `docs/security/audit-retention-and-incident-triage.md`, `README.md`

- [ ] Verify no `workplace.example` fallback remains outside test fixtures.
- [ ] Verify invitation plaintext appears only in the immediate create response and controlled UI handoff; it is absent from list/detail DTOs, logs, audit metadata and tests that assert response shape.
- [ ] Verify local admin cookies cannot satisfy customer routes and customer bearer tokens cannot satisfy platform routes without the platform scope.
- [ ] Verify every workspace query and mutation is scoped, every Graph target is validated against the current tenant, and error responses do not leak raw Graph payloads.
- [ ] Verify changed files have focused tests and run the complete commands from Task 7 with exit code 0. Record unavoidable external limitations without claiming them as passed.
- [ ] Commit only documentation/test corrections from the review, then record the final commit range and local verification output.

## Plan Self-Review

- **Spec coverage:** Tasks 1–3 cover local configuration, invitation redemption and consent/connection states; Tasks 4–6 cover all customer V1 actions, RBAC and PIM; Task 7 covers complete local and opt-in real-tenant verification; Task 8 covers security and release evidence. Azure deployment is intentionally absent.
- **Placeholder scan:** No implementation step relies on TODO/TBD language or unspecified “appropriate” behavior. All new response shapes, routes, files and commands are named.
- **Type consistency:** Invitation, consent, capability, mutation and test interfaces are defined before the consuming UI tasks. Existing current-branch types are reused where stated.
- **Review focus coverage:** Each review focus line has explicit tests in Tasks 1–7, with cross-cutting verification repeated in Task 8.
