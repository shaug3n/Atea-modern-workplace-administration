# Atea Unified Workplace MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local-first, Atea-hosted MVP that lets a customer administrator or an authorized Atea B2B guest administer one Microsoft 365 tenant through a unified, permission-aware workspace. The MVP covers tenant onboarding, users, licenses, group membership, directory-role/PIM visibility and the approved user lifecycle actions. It must run locally against a real test tenant and use the same containerized application and configuration contracts when later deployed to Azure.

**Architecture:** React SPA authenticates users with Microsoft Entra ID and calls an ASP.NET Core BFF/API. The API resolves the active workspace from verified token claims and server-side membership, acquires delegated Microsoft Graph tokens on behalf of the signed-in user, evaluates effective Entra permissions, executes Graph operations, and records safe platform audit events. PostgreSQL stores only workspace/platform metadata and audit data; Microsoft Graph remains the source of truth for directory data. Docker Compose is the local runtime. Azure Container Apps, Azure Database for PostgreSQL, Key Vault, Container Registry, Application Insights and controlled HTTPS ingress are the production runtime.

**Tech Stack:** React + TypeScript + Vite, `@azure/msal-browser`, `@azure/msal-react`, ASP.NET Core 9 Web API, Microsoft.Identity.Web, Microsoft Graph SDK for .NET, Entity Framework Core with PostgreSQL/Npgsql, xUnit, FluentAssertions, Testcontainers for PostgreSQL, Vitest, React Testing Library, Playwright, Docker Compose, Bicep, Azure Container Apps, Azure Database for PostgreSQL Flexible Server, Azure Key Vault, Azure Container Registry, Application Insights and Log Analytics.

**Spec:** [docs/superpowers/specs/2026-09-17-atea-unified-workplace-design.md](/Users/sondre.haugen/Digital%20workplace%20unified%20workplace%20tool/docs/superpowers/specs/2026-09-17-atea-unified-workplace-design.md)

## Global Constraints

- English-only UI in v1; all visible strings live in a typed frontend message catalog rather than inline JSX text.
- Light Atea operations theme is the default. Dark mode is a user-controlled preference and must use semantic token mappings, not a separate component implementation.
- Use the Atea logo artwork and bundled Inter font assets supplied by the Atea webapp design skill. Do not redraw the logo or invent a product identity that competes with Atea.
- Entra ID is the only platform authentication mechanism. Do not add local passwords, customer self-registration or a hidden Atea super-admin path.
- Atea operators can act on a customer tenant only as B2B guests with customer-granted Entra roles and active delegated access. Platform workspace roles grant only onboarding/configuration/support metadata permissions.
- Microsoft Graph delegated authorization and the signed-in user’s effective tenant roles, custom permissions, administrative-unit scope, PIM state and tenant policy are authoritative. Never equate an application role with Microsoft 365 directory authority.
- The frontend never calls Graph directly and never receives, persists or logs a Graph access token. MSAL may keep the API token in memory only; the API performs on-behalf-of Graph token acquisition.
- The active workspace is derived server-side from verified `tid`, `oid`, workspace membership and the customer tenant context. No endpoint accepts a client-selected tenant ID as an authorization input.
- v1 has no broad replicated directory cache, devices/Intune, advanced statistics, bulk administration, password reset, MFA reset, session revocation, authentication-method management, Azure resource-role PIM or group PIM.
- Every tenant-owned platform record carries an immutable workspace ID. Every repository query and mutation is workspace-scoped, and cross-tenant object access must fail closed.
- Never store or log access tokens, refresh tokens, client secrets, temporary passwords, MFA data or raw authorization headers. Audit before/after data is limited to approved non-secret fields.
- Mutations require explicit confirmation, capability enforcement at the API, Graph error translation and an idempotency key. Do not automatically repeat a mutation after a `409`.
- Use PostgreSQL locally and in Azure. Use the same migrations, container image contracts and environment variable names in both environments.
- Use one Atea-approved EU/EEA Azure region for v1. Keep region-specific values in deployment parameters.
- Real test-tenant validation is required for delegated consent, customer roles, Atea B2B access, Global Reader, User Administrator, missing consent, PIM activation, approval, MFA/Conditional Access and revocation. Mock Graph only for isolated unit/UI tests.
- Do not commit secrets, tenant credentials, local `.env` files or generated build output.

---

## Repository Layout and Component Responsibilities

Create this structure before feature work. Keep the frontend and API independently testable while allowing the API to serve the built SPA in the production container.

```text
src/
  Api/
    Features/
      Workspaces/
      Users/
      Licenses/
      Groups/
      Roles/
      Pim/
      Audit/
    Authorization/
    Infrastructure/
      Graph/
      Persistence/
      Observability/
    Program.cs
  Web/
    src/
      app/
      components/
      features/
        overview/
        users/
        licenses/
        audit/
        workspace-settings/
      auth/
      capabilities/
      styles/
      messages/
tests/
  Api.UnitTests/
  Api.IntegrationTests/
  Web.UnitTests/
  Web.E2E/
infra/
  main.bicep
  modules/
  parameters/
docs/
  security/
  testing/
  operations/
docker-compose.yml
Dockerfile
README.md
```

Responsibilities:

- `src/Api/Features` contains endpoint contracts, request validation and application services. Endpoints do not call Graph SDK types directly.
- `src/Api/Authorization` contains the verified user/workspace context, capability states and API authorization handlers.
- `src/Api/Infrastructure/Graph` contains only delegated Graph token acquisition, focused Graph adapters, Graph DTO mapping and error translation.
- `src/Api/Infrastructure/Persistence` contains EF Core entities, the `WorkplaceDbContext`, migrations and workspace-scoped repositories.
- `src/Web/src/features` contains route-level screens and feature-specific data hooks. It consumes API DTOs and capability states only.
- `src/Web/src/components` contains reusable shell, table, status, confirmation, error and permission-state components.
- `tests/Api.IntegrationTests` uses a disposable PostgreSQL container and fake Graph transport for API integration; real test-tenant checks live in separately opt-in tests.
- `tests/Web.E2E` uses Playwright with seeded API fixtures for deterministic UI checks and a separate real-tenant profile for consent/RBAC/PIM validation.
- `infra` contains Bicep only; no environment secret values are committed.

## Task 1: Bootstrap the solution and reproducible local runtime

Create the empty repository’s buildable baseline and make the local developer workflow executable before adding business behavior.

Files to create or update:

- `Atea.UnifiedWorkplace.sln`
- `src/Api/Atea.UnifiedWorkplace.Api.csproj`
- `src/Api/Program.cs`
- `src/Web/package.json`, `src/Web/tsconfig.json`, `src/Web/vite.config.ts`
- `src/Web/src/main.tsx`, `src/Web/src/app/App.tsx`
- `tests/Api.UnitTests/Api.UnitTests.csproj`
- `tests/Api.IntegrationTests/Api.IntegrationTests.csproj`
- `tests/Web.UnitTests/package.json`
- `tests/Web.E2E/package.json`
- `docker-compose.yml`, `Dockerfile`, `.dockerignore`, `.env.example`
- `.editorconfig`, `.gitignore`, `README.md`

- [ ] Add a failing API smoke test named `ApiStartsAndExposesHealthEndpoint` in `tests/Api.IntegrationTests/HealthEndpointTests.cs` that starts the API test host and expects `GET /health` to return `200` with `{ "status": "ok" }`.
- [ ] Run `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj`; confirm it fails because the solution and health endpoint do not yet exist.
- [ ] Create the .NET solution/projects and add only the packages needed for ASP.NET Core, xUnit, `Microsoft.AspNetCore.Mvc.Testing`, FluentAssertions and test infrastructure.
- [ ] Add the Vite React TypeScript app with a shell that renders `Atea Unified Workplace` and a `GET /health` proxy configuration for local API development.
- [ ] Implement `GET /health` with the exact JSON response and no authentication requirement so Docker health probes can use it.
- [ ] Add `docker-compose.yml` with `api`, `web` and `postgres` services, PostgreSQL health checking, a named data volume and an explicit `5432` container port. The API must consume `ConnectionStrings__WorkplaceDb` from the compose environment.
- [ ] Add a multi-stage `Dockerfile` that builds the React app, publishes the API, copies the frontend build into the API static-files directory and runs the API as a non-root user.
- [ ] Run `dotnet test`, `npm ci` plus `npm run test`, and `docker compose config`; confirm the health test and frontend smoke test pass and the compose file is valid.
- [ ] Document exact local commands, required tool versions, test commands and the fact that `.env` is local-only in `README.md`.
- [ ] Commit as `build: bootstrap local workplace solution`.

## Task 2: Add Entra authentication and verified workspace context

Implement the authentication boundary before any tenant data endpoint exists. The browser authenticates to the multitenant workforce app registration and sends an API bearer token; the API validates it and resolves a verified server-side user context.

Files to create or update:

- `src/Api/Authorization/AuthenticatedUser.cs`
- `src/Api/Authorization/WorkspaceContext.cs`
- `src/Api/Authorization/WorkspaceContextMiddleware.cs`
- `src/Api/Authorization/PlatformAuthorization.cs`
- `src/Api/Program.cs`, `src/Api/appsettings.json`, `src/Api/appsettings.Development.json`
- `src/Web/src/auth/msalConfig.ts`, `src/Web/src/auth/AuthProvider.tsx`, `src/Web/src/auth/useApi.ts`
- `tests/Api.UnitTests/Authorization/WorkspaceContextTests.cs`
- `tests/Api.IntegrationTests/Authorization/AuthenticationTests.cs`
- `tests/Web.UnitTests/auth/AuthProvider.test.tsx`
- `docs/security/entra-app-registration.md`

- [ ] Add failing unit cases for: missing `tid`, missing `oid`, wrong audience, unknown workspace membership, and a valid customer-tenant identity. Use a fixed `AuthenticatedUser` fixture and assert the exact failure reason enum rather than matching exception text.
- [ ] Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~WorkspaceContextTests`; confirm the cases fail before the resolver exists.
- [ ] Configure `AddMicrosoftIdentityWebApi` with issuer validation for multitenant Entra workforce tokens, the API audience, and tenant-independent authority. Reject unsigned, expired, wrong-audience and wrong-issuer tokens with `401`.
- [ ] Define `AuthenticatedUser` with `TenantId`, `ObjectId`, `UserPrincipalName`, `DisplayName`, `UserType` and optional `HomeTenantId`; do not use a client-supplied tenant identifier.
- [ ] Define `IWorkspaceContextAccessor` and `WorkspaceContextMiddleware`. Resolve the workspace using the verified token tenant, object ID and membership repository; attach the context to the request and return `403 workspace_membership_required` when no membership matches.
- [ ] Configure the frontend MSAL authority, client ID, API scope and redirect URI entirely from `VITE_` environment values. Set token cache location to memory and never write Graph tokens to browser storage.
- [ ] Implement `AuthProvider` with sign-in, sign-out, silent API-token acquisition and an accessible sign-in/error screen. Add an API client that attaches only the API access token.
- [ ] Add an integration test that proves unauthenticated API calls return `401`, authenticated users with no membership return `403`, and a valid fixture can call `/api/session` without exposing token contents.
- [ ] Run API unit/integration tests and frontend tests; confirm no test output contains `access_token`, `refresh_token`, `client_secret` or `Authorization: Bearer` values.
- [ ] Document the separate development and production app registrations, approved redirect URIs, API scope name and admin-consent handoff in `docs/security/entra-app-registration.md`.
- [ ] Commit as `feat: establish entra authentication boundary`.

## Task 3: Add tenant-isolated persistence and platform workspace administration

Store only platform metadata in PostgreSQL and make workspace isolation a reusable invariant. Add the narrow platform role needed for Atea provisioning without granting Microsoft 365 directory authority.

Files to create or update:

- `src/Api/Infrastructure/Persistence/WorkplaceDbContext.cs`
- `src/Api/Infrastructure/Persistence/Entities/Workspace.cs`
- `src/Api/Infrastructure/Persistence/Entities/WorkspaceMembership.cs`
- `src/Api/Infrastructure/Persistence/Entities/TenantConnection.cs`
- `src/Api/Infrastructure/Persistence/Entities/WorkspaceSettings.cs`
- `src/Api/Infrastructure/Persistence/Entities/PlatformInvitation.cs`
- `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs`
- `src/Api/Infrastructure/Persistence/Repositories/WorkspaceRepository.cs`
- `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`
- `src/Api/Features/Workspaces/WorkspaceContracts.cs`
- `src/Api/Authorization/PlatformAuthorization.cs`
- `tests/Api.UnitTests/Persistence/WorkspaceIsolationTests.cs`
- `tests/Api.IntegrationTests/Persistence/WorkspaceRepositoryTests.cs`
- `tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs`
- `docs/security/data-isolation.md`

- [ ] Add failing repository tests that create two workspaces and assert a repository constructed with workspace A cannot read, update or delete workspace B records, even when given B’s object ID.
- [ ] Run the repository tests; confirm they fail because no entities, context or workspace-scoped repository exists.
- [ ] Define entities with these fields: `Workspace(Id, TenantId, DisplayName, ConnectionStatus, CreatedAt, UpdatedAt)`; `WorkspaceMembership(Id, WorkspaceId, TenantObjectId, Email, PlatformRole, IsAteaOperator, CreatedAt)`; `TenantConnection(WorkspaceId, Status, ConsentScopesJson, LastVerifiedAt, LastFailureCategory, UpdatedAt)`; `WorkspaceSettings(WorkspaceId, DefaultTheme, EnabledModulesJson, DefaultColumnsJson, SupportInstructions)`; and `PlatformInvitation(Id, WorkspaceId, Email, DisplayName, NonceHash, ExpiresAt, RedeemedAt, CreatedAt)`.
- [ ] Add unique indexes on `Workspace.TenantId`, `(WorkspaceMembership.WorkspaceId, WorkspaceMembership.TenantObjectId)`, and `PlatformInvitation.NonceHash`; configure all timestamps as UTC `DateTimeOffset` values.
- [ ] Implement `IWorkspaceRepository` methods that all require `WorkspaceId`: `GetAsync`, `AddAsync`, `UpdateConnectionAsync`, `AddMembershipAsync` and `GetMembershipAsync`. Do not expose an unscoped `GetByIdAsync` method.
- [ ] Register EF Core/Npgsql, create the initial migration, and make startup apply migrations only in Development. Production migration execution will be a deployment step in Task 13.
- [ ] Implement platform-only authorization from a configured allowlist of Atea platform-admin object IDs for workspace provisioning and membership metadata. Keep this separate from Microsoft Graph capability checks.
- [ ] Add `POST /api/platform/workspaces` with `{ tenantId, displayName }`, server-side tenant ID validation, duplicate prevention and `awaiting_invitation` initial state. Return a public workspace DTO without secrets.
- [ ] Add `POST /api/platform/workspaces/{workspaceId}/memberships` with `{ tenantObjectId, email, platformRole, isAteaOperator }`. Require platform authorization and validate that the route workspace belongs to the caller’s provisioning scope.
- [ ] Add an authenticated `GET /api/workspaces/current` endpoint that returns workspace metadata and connection state only after `WorkspaceContextMiddleware` has resolved the tenant.
- [ ] Run tests using Testcontainers PostgreSQL and `docker compose up postgres`; confirm the cross-tenant tests pass against PostgreSQL, not only an in-memory provider.
- [ ] Document the workspace isolation invariant, approved stored fields and platform-role limitation in `docs/security/data-isolation.md`.
- [ ] Commit as `feat: add isolated workspace persistence`.

## Task 4: Implement onboarding, invitation instructions and connection health

Build the Atea-provisioned and Atea-led connection flows. Invitation secrets are one-time values; the API stores only a hash and the UI explains each connection state.

Files to create or update:

- `src/Api/Features/Workspaces/OnboardingService.cs`
- `src/Api/Features/Workspaces/InvitationService.cs`
- `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`
- `src/Api/Infrastructure/Graph/IConnectionHealthReader.cs`
- `src/Api/Infrastructure/Graph/ConnectionHealthReader.cs`
- `src/Api/Infrastructure/Graph/GraphScopeCatalog.cs`
- `src/Web/src/features/overview/OverviewPage.tsx`
- `src/Web/src/features/workspace-settings/OnboardingPage.tsx`
- `src/Web/src/components/ConnectionStatusCard.tsx`
- `src/Web/src/messages/en.ts`
- `tests/Api.UnitTests/Workspaces/OnboardingStateTests.cs`
- `tests/Api.UnitTests/Workspaces/InvitationServiceTests.cs`
- `tests/Api.IntegrationTests/Workspaces/OnboardingEndpointTests.cs`
- `tests/Web.UnitTests/features/overview/ConnectionStatusCard.test.tsx`
- `docs/security/graph-permission-matrix.md`
- `docs/testing/test-tenant.md`

- [ ] Add failing state-transition tests for every allowed transition: `awaiting_invitation -> consent_required`, `consent_required -> connected`, `connected -> permission_incomplete`, `connected -> temporarily_unavailable`, `connected -> consent_revoked`, and any failure state returning to `consent_required` after remediation. Reject invalid transitions.
- [ ] Run the state tests; confirm they fail before the transition service exists.
- [ ] Implement `InvitationService.CreateAsync(workspaceId, email, displayName, expiresAt)` using a cryptographically random nonce, storing only `SHA-256(nonce)` and returning the plaintext invitation URL exactly once to the initiating Atea platform administrator. Never log the URL or nonce.
- [ ] Implement authenticated invite redemption that requires Entra sign-in, verifies the token tenant equals the workspace tenant, verifies the invited email or approved object ID, atomically sets `RedeemedAt`, creates the workspace membership and moves the workspace to `consent_required`.
- [ ] Add `GET /api/workspaces/current/connection-health` and `POST /api/workspaces/current/connection-health/check`. The check must call the focused Graph connection reader with delegated access, record only scopes, status and correlation IDs, and map consent/permission failures to the specified connection states.
- [ ] Add `POST /api/workspaces/current/consent/start` that returns a frontend-safe authorization URL or challenge descriptor for the minimum documented delegated scopes. The API must not return a Graph token.
- [ ] Render the overview with explicit badges and next actions for all seven connection states. Do not show a generic “connected” badge when the last check is stale, revoked or permission-incomplete.
- [ ] Add a connection-scope matrix documenting the exact v1 delegated scopes, the Graph operation each scope supports, whether admin consent is required, and the least-privileged tenant role needed. Verify the matrix against Microsoft Graph’s official permission reference during implementation and add the test-tenant values to the runbook.
- [ ] Run unit, API integration and frontend tests; confirm an invitation nonce is never present in database snapshots, structured logs or API responses after redemption.
- [ ] Document the customer-admin handoff and the Atea B2B guest completion path in `docs/testing/test-tenant.md`.
- [ ] Commit as `feat: add tenant onboarding and connection health`.

## Task 5: Build the delegated Microsoft Graph adapter boundary

Create focused interfaces so feature code never depends on Graph SDK request details. Centralize delegated token acquisition, request correlation, pagination, throttling and error mapping.

Files to create or update:

- `src/Api/Infrastructure/Graph/IGraphTokenProvider.cs`
- `src/Api/Infrastructure/Graph/DelegatedGraphClientFactory.cs`
- `src/Api/Infrastructure/Graph/GraphDirectoryReader.cs`
- `src/Api/Infrastructure/Graph/GraphUserLifecycle.cs`
- `src/Api/Infrastructure/Graph/GraphGroupMembershipService.cs`
- `src/Api/Infrastructure/Graph/GraphLicenseService.cs`
- `src/Api/Infrastructure/Graph/GraphRoleAndPimService.cs`
- `src/Api/Infrastructure/Graph/GraphErrorMapper.cs`
- `src/Api/Infrastructure/Graph/GraphOperationResult.cs`
- `src/Api/Infrastructure/Graph/IGraphTransport.cs`
- `tests/Api.UnitTests/Graph/GraphErrorMapperTests.cs`
- `tests/Api.UnitTests/Graph/GraphRetryPolicyTests.cs`
- `tests/Api.IntegrationTests/Graph/FakeGraphTransport.cs`
- `tests/Api.IntegrationTests/Graph/DelegatedGraphAdapterTests.cs`
- `docs/security/graph-permission-matrix.md`

- [ ] Add failing mapper tests for Graph `401`, `403`, `404`, `409`, `429` with `Retry-After`, `5xx`, network failure, consent challenge and Conditional Access challenge. Assert application categories `unauthenticated`, `not_authorized`, `not_found`, `conflict`, `throttled`, `temporarily_unavailable`, `consent_required` and `mfa_required`.
- [ ] Run the mapper tests; confirm they fail before the mapper and result types exist.
- [ ] Define these stable interfaces:

  ```csharp
  public interface IDelegatedGraphClientFactory
  {
      Task<GraphClientLease> CreateForCurrentUserAsync(
          IReadOnlyCollection<string> scopes,
          CancellationToken cancellationToken);
  }

  public interface IUserDirectoryReader
  {
      Task<PagedResult<UserSummary>> SearchAsync(
          UserSearchQuery query,
          CancellationToken cancellationToken);
      Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken);
  }

  public interface IGraphMutationExecutor
  {
      Task<GraphOperationResult> ExecuteAsync(
          GraphMutation mutation,
          string idempotencyKey,
          CancellationToken cancellationToken);
  }
  ```
- [ ] Implement `DelegatedGraphClientFactory` with Microsoft.Identity.Web on-behalf-of token acquisition for the currently authenticated user and the requested least-privilege scopes. Reject empty scope sets and never expose the acquired token outside the Graph client lease.
- [ ] Implement `IGraphTransport` as the only class allowed to send Graph HTTP requests. Add request correlation, bounded retry for `429`/transient `5xx` only, `Retry-After` handling and no automatic retry for `409` or permission failures.
- [ ] Implement focused adapters for directory reads, user lifecycle, groups, licenses, roles/PIM and connection health. Each adapter returns application DTOs/results, not Graph SDK models.
- [ ] Add fake-transport integration tests proving pagination, selected-field projection, request scopes, correlation ID capture and all error mappings.
- [ ] Run API unit/integration tests and inspect structured test logs; confirm Graph tokens and `Authorization` headers are absent.
- [ ] Commit as `feat: add delegated graph adapter boundary`.

## Task 6: Implement effective capability evaluation and PIM-aware API guards

Expose the signed-in user’s effective capabilities without inventing a second directory authorization system. The evaluator may optimize obvious role combinations, but every mutation remains Graph-authoritative and can return a more specific denial or PIM state.

Files to create or update:

- `src/Api/Authorization/Capability.cs`
- `src/Api/Authorization/CapabilitySnapshot.cs`
- `src/Api/Authorization/CapabilityEvaluator.cs`
- `src/Api/Authorization/RequireCapabilityAttribute.cs`
- `src/Api/Infrastructure/Graph/IGraphAuthorizationSnapshotReader.cs`
- `src/Api/Infrastructure/Graph/GraphAuthorizationSnapshotReader.cs`
- `src/Api/Features/Authorization/CapabilityEndpoints.cs`
- `src/Api/Features/Pim/PimStateMapper.cs`
- `src/Web/src/capabilities/capabilityTypes.ts`
- `src/Web/src/capabilities/useCapabilities.ts`
- `src/Web/src/components/PermissionState.tsx`
- `tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs`
- `tests/Api.UnitTests/Pim/PimStateMapperTests.cs`
- `tests/Api.IntegrationTests/Authorization/CapabilityEndpointTests.cs`
- `tests/Web.UnitTests/capabilities/PermissionState.test.tsx`
- `docs/security/entra-role-capability-matrix.md`

- [ ] Add failing evaluator tests for: Global Reader with `users.view=allowed` and mutation states `read_only`; User Administrator with user lifecycle states `allowed`; no directory read permission with user sections `hidden`; consent missing with `consent_required`; eligible inactive role with `pim_activation_required`; approval/MFA/eligibility failures with their exact PIM states.
- [ ] Run the evaluator tests; confirm they fail before capability types, role catalog and PIM mapping exist.
- [ ] Define the capability names and states from the approved spec as string-backed enums/unions. Return `{ capability, state, reasonCode, requiredRoleTemplateId?, pim? }` for every capability, never only a boolean.
- [ ] Implement a versioned role catalog keyed by stable Entra role template IDs and verified Graph scope data. Keep display names as localized presentation data; do not authorize by display-name string.
- [ ] Implement `IGraphAuthorizationSnapshotReader.ReadAsync` to obtain the signed-in user object, effective directory-role assignments and role activation/PIM information needed by the current workspace. Keep administrative-unit scope and tenant policy flags in the snapshot when Graph returns them.
- [ ] Implement `CapabilityEvaluator.Evaluate(snapshot, workspaceMembership)` so platform-only settings use the platform role and Microsoft 365 capabilities use the intersection of delegated scopes, active role state and tenant policy. Mark unknown/failed reads as `temporarily_unavailable`, not `allowed`.
- [ ] Add `GET /api/capabilities` returning the complete snapshot for the resolved workspace. Add an API guard that returns a structured `403` problem response with `capability`, `state`, `reasonCode`, `requiredRole` and `nextStep` before a disallowed operation reaches its application service.
- [ ] Implement the React capability hook and `PermissionState` component for hidden, read-only, disabled, consent, PIM, approval and MFA states. Ensure the component can render an accessible explanation and a next-step link.
- [ ] Run API/frontend tests; confirm a capability response cannot grant a mutation when the Graph snapshot is unavailable and that a UI-hidden feature is still denied when its endpoint is called directly.
- [ ] Document the verified role-to-capability matrix and test-tenant role assignments in `docs/security/entra-role-capability-matrix.md`.
- [ ] Commit as `feat: enforce tenant capabilities and pim states`.

## Task 7: Build the Atea shell, routing and visual system

Implement the approved Atea light operations desk with a reliable dark-mode toggle and capability-aware navigation before filling the feature pages.

Files to create or update:

- `src/Web/src/app/App.tsx`, `src/Web/src/app/routes.tsx`
- `src/Web/src/components/AppShell.tsx`
- `src/Web/src/components/PrimaryNav.tsx`
- `src/Web/src/components/TenantContextHeader.tsx`
- `src/Web/src/components/ThemeToggle.tsx`
- `src/Web/src/components/StatusBadge.tsx`
- `src/Web/src/styles/atea-tokens.css`
- `src/Web/src/styles/theme.css`
- `src/Web/src/messages/en.ts`
- `tests/Web.UnitTests/components/AppShell.test.tsx`
- `tests/Web.UnitTests/components/ThemeToggle.test.tsx`
- `tests/Web.UnitTests/components/PrimaryNav.test.tsx`
- `docs/design/atea-ui-usage.md`

- [ ] Add failing component tests for default light mode, dark-mode persistence in the current user preference service, keyboard-accessible toggle labeling, Atea logo rendering, and hiding Workspace settings when `workspace.configure` is not allowed.
- [ ] Run `npm run test -- --run`; confirm the tests fail before the shell and theme provider exist.
- [ ] Add the bundled Inter font via `@font-face`, use the original Atea logo asset, define semantic light/dark tokens for surfaces, text, borders, links, focus, success, warning, error and primary action, and avoid gradients.
- [ ] Implement `ThemeProvider` with `light | dark` state, a system preference fallback only before the first user preference exists, and an explicit `data-theme` attribute. Store only the preference value in the platform user-preferences endpoint or an in-memory development fallback; do not store tenant credentials.
- [ ] Implement routes `/overview`, `/users`, `/users/:userId`, `/licenses`, `/audit`, `/workspace-settings` with a route guard that consumes `/api/capabilities`.
- [ ] Implement the shell header with Atea identity, current tenant/workspace name, connection freshness badge, signed-in user menu and theme toggle. Keep status text readable without relying on colour alone.
- [ ] Implement navigation groups for Overview, Users, Licenses, Audit activity and conditionally Workspace settings. Hidden sections must not be reachable through navigation, while direct routes still render a permission state.
- [ ] Add narrow-width and reduced-motion CSS behavior, visible keyboard focus, skip-to-content link, semantic landmarks and screen-reader labels.
- [ ] Run frontend unit tests, `npm run build` and a Playwright shell smoke test at desktop and narrow viewport sizes.
- [ ] Document the token usage, asset provenance, light/dark mapping and accessibility decisions in `docs/design/atea-ui-usage.md`.
- [ ] Commit as `feat: add atea accessible application shell`.

## Task 8: Implement the live users directory

Deliver the first end-to-end read flow backed by paginated Graph data and capability-aware fields/actions.

Files to create or update:

- `src/Api/Features/Users/UserContracts.cs`
- `src/Api/Features/Users/UserQueryService.cs`
- `src/Api/Features/Users/UserEndpoints.cs`
- `src/Web/src/features/users/usersApi.ts`
- `src/Web/src/features/users/UsersPage.tsx`
- `src/Web/src/features/users/UserFilters.tsx`
- `src/Web/src/features/users/UsersTable.tsx`
- `src/Web/src/components/DataFreshness.tsx`
- `src/Web/src/components/AsyncState.tsx`
- `tests/Api.UnitTests/Users/UserQueryServiceTests.cs`
- `tests/Api.IntegrationTests/Users/UserEndpointsTests.cs`
- `tests/Web.UnitTests/features/users/UsersTable.test.tsx`
- `tests/Web.UnitTests/features/users/UserFilters.test.tsx`
- `tests/Web.E2E/users-directory.spec.ts`

- [ ] Add failing API tests for a paginated search, supported filters (`search`, account status, tenant role, license, user type), selected field projection and Graph `404`/`429` translation.
- [ ] Run the API tests; confirm they fail before the user query service and endpoint exist.
- [ ] Define `UserSearchQuery` with validated page size `1..100`, opaque `continuationToken`, search text, and the approved filter fields. Define `UserSummary` with only fields the endpoint is allowed to expose.
- [ ] Implement `GET /api/users` through `IUserDirectoryReader.SearchAsync`, pass the server-resolved workspace and current-user context, and return `{ items, continuationToken, fetchedAt, freshness, partialData }`.
- [ ] Ensure Graph queries use `$select` for approved fields, pass the continuation link only through a signed/opaque server token, and never preload or cache the full directory.
- [ ] Implement the Users page with debounced search, filter chips, pagination controls, refresh, loading skeleton, no-results state, no-permission state, stale/unavailable banner and responsive table behavior.
- [ ] Hide sensitive columns when their capability is `hidden`; keep them visible but clearly unavailable when the result is `read_only` or `temporarily_unavailable`.
- [ ] Add row navigation to `/users/:userId` and render mutation controls only from capability state, not from a hard-coded “admin” flag.
- [ ] Run API/frontend tests and the Playwright users-directory spec with seeded pages. Verify the browser makes no request to a Graph hostname.
- [ ] Commit as `feat: add permission-aware users directory`.

## Task 9: Implement user details, licenses, groups, roles and PIM read views

Build the detail page as independent sections so one missing permission does not blank the entire user record.

Files to create or update:

- `src/Api/Features/Users/UserDetailService.cs`
- `src/Api/Features/Users/UserDetailEndpoints.cs`
- `src/Api/Features/Licenses/LicenseEndpoints.cs`
- `src/Api/Features/Groups/GroupEndpoints.cs`
- `src/Api/Features/Roles/RoleEndpoints.cs`
- `src/Api/Features/Pim/PimEndpoints.cs`
- `src/Web/src/features/users/UserDetailPage.tsx`
- `src/Web/src/features/users/IdentitySection.tsx`
- `src/Web/src/features/users/JobInformationSection.tsx`
- `src/Web/src/features/users/LicensesSection.tsx`
- `src/Web/src/features/users/GroupsSection.tsx`
- `src/Web/src/features/users/RolesAndPimSection.tsx`
- `src/Web/src/features/users/userDetailApi.ts`
- `tests/Api.UnitTests/Users/UserDetailServiceTests.cs`
- `tests/Api.IntegrationTests/Users/UserDetailEndpointTests.cs`
- `tests/Web.UnitTests/features/users/UserDetailPage.test.tsx`
- `tests/Web.E2E/user-detail.spec.ts`

- [ ] Add failing endpoint tests for a complete permitted detail view, independent `403`/PIM/consent states per section, a removed user (`404`) and stale data indicators.
- [ ] Run the endpoint tests; confirm they fail before the detail service and endpoints exist.
- [ ] Define DTOs for `UserDetails`, `AssignedLicense`, `GroupMembership`, `DirectoryRoleAssignment`, `PimEligibility` and `SectionAccessState`; exclude raw Graph payloads and unsupported fields.
- [ ] Implement `GET /api/users/{userObjectId}` and section endpoints for licenses, groups, roles and PIM. Each endpoint must verify the route object belongs to the current tenant and return the same structured authorization state used by `/api/capabilities`.
- [ ] Implement the detail page sections with identity/job/account fields, assigned licenses, groups, role assignments and PIM state. Use a section-level loading/error/permission component.
- [ ] Show inactive eligible roles with the role name, required capability, activation availability, approval/MFA/justification/time-limit information and an explicit activation action whose typed API client contract is completed in Task 11.
- [ ] Show directory-synchronized or externally managed users as read-only with the source-of-authority reason. Do not render buttons that predictably fail.
- [ ] Run API/frontend tests and the Playwright detail spec at Global Reader and User Administrator fixtures; confirm Global Reader can view permitted sections but cannot see enabled mutation controls.
- [ ] Commit as `feat: add user detail and access state views`.

## Task 10: Implement safe user lifecycle mutations

Add the approved lifecycle actions with explicit review, idempotency, audit hooks and safe temporary-password handling.

Files to create or update:

- `src/Api/Features/Users/UserCommandContracts.cs`
- `src/Api/Features/Users/UserCommandService.cs`
- `src/Api/Features/Users/UserCommandEndpoints.cs`
- `src/Api/Features/Groups/GroupMembershipService.cs`
- `src/Api/Features/Licenses/LicenseAssignmentService.cs`
- `src/Api/Infrastructure/Security/IdempotencyService.cs`
- `src/Web/src/features/users/UserEditDialog.tsx`
- `src/Web/src/features/users/UserCreateDialog.tsx`
- `src/Web/src/features/users/GroupMembershipDialog.tsx`
- `src/Web/src/features/users/LicenseAssignmentDialog.tsx`
- `src/Web/src/components/ConfirmationDialog.tsx`
- `tests/Api.UnitTests/Users/UserCommandServiceTests.cs`
- `tests/Api.UnitTests/Security/IdempotencyServiceTests.cs`
- `tests/Api.IntegrationTests/Users/UserMutationEndpointTests.cs`
- `tests/Web.UnitTests/features/users/UserMutationDialogs.test.tsx`
- `tests/Web.E2E/user-lifecycle.spec.ts`
- `docs/security/user-lifecycle-permissions.md`

- [ ] Add failing service tests for create, edit, disable, reactivate, add/remove group, assign/remove license, missing capability, source-of-authority read-only, duplicate idempotency key and Graph conflict. Assert no command reaches Graph when capability is not allowed.
- [ ] Run the service tests; confirm they fail before command contracts and services exist.
- [ ] Define command contracts with exact approved fields: `CreateUserCommand(displayName, givenName, surname, userPrincipalName, mailNickname, jobTitle, department, officeLocation, mobilePhone, usageLocation, accountEnabled)`, `UpdateUserCommand(...)`, `SetAccountEnabledCommand(enabled)`, `GroupMembershipCommand(groupObjectId)`, and `LicenseAssignmentCommand(skuId, disabledPlans)`.
- [ ] Require `Idempotency-Key` on every POST/PATCH mutation, scope the key to workspace + actor + operation + target, persist only a safe request fingerprint and result metadata, and return the original result for an exact replay. Return `409 idempotency_key_reused` for a changed payload.
- [ ] Implement API routes: `POST /api/users`, `PATCH /api/users/{id}`, `POST /api/users/{id}/disable`, `POST /api/users/{id}/reactivate`, `POST|DELETE /api/users/{id}/groups/{groupId}`, and `POST|DELETE /api/users/{id}/licenses/{skuId}`. Protect each route with its specific capability.
- [ ] Implement user creation so a temporary password is generated only in memory for the one Graph request, `forceChangePasswordNextSignIn=true` is set, and the plaintext is returned once in a `TemporaryCredentialNotice` only after Graph success. Never persist, audit or log the password; return no credential on any failure.
- [ ] Implement review/confirmation DTOs and frontend dialogs that show target, proposed change, required capability, source-of-authority/policy limitation, audit notice and a final confirmation control. Destructive disable must use a second explicit confirmation phrase.
- [ ] Implement safe handling for directory-synchronized/external users: return `source_of_authority_read_only` without attempting mutation.
- [ ] Add API integration tests proving a Global Reader receives structured `403 read_only`, a User Administrator can perform allowed fixture mutations, and duplicate browser submission produces one Graph mutation and one success result.
- [ ] Add Playwright coverage for confirmation, validation, success refresh, conflict, throttling, temporary-password one-time display and permission explanation.
- [ ] Run all API/frontend/E2E tests and inspect logs/database fixtures to prove no temporary password or token is retained.
- [ ] Document least-privilege delegated scopes and tenant role expectations for each action in `docs/security/user-lifecycle-permissions.md`.
- [ ] Commit as `feat: add safe user lifecycle administration`.

## Task 11: Implement explicit directory-role PIM activation and guided handoff

Support Entra directory-role PIM in a deliberate, user-visible flow. Never silently activate a role or treat eligibility as active authority.

Files to create or update:

- `src/Api/Features/Pim/PimContracts.cs`
- `src/Api/Features/Pim/PimService.cs`
- `src/Api/Features/Pim/PimEndpoints.cs`
- `src/Api/Infrastructure/Graph/GraphRoleAndPimService.cs`
- `src/Web/src/features/pim/PimActivationDialog.tsx`
- `src/Web/src/features/pim/PimGuidedHandoff.tsx`
- `src/Web/src/features/users/RolesAndPimSection.tsx`
- `tests/Api.UnitTests/Pim/PimServiceTests.cs`
- `tests/Api.IntegrationTests/Pim/PimEndpointTests.cs`
- `tests/Web.UnitTests/features/pim/PimActivationDialog.test.tsx`
- `tests/Web.E2E/pim-activation.spec.ts`
- `docs/testing/pim-test-matrix.md`

- [ ] Add failing PIM service tests for active role, eligible/inactive role, not eligible, approval required, MFA required, justification required, policy denial, consent missing, already pending request and transient Graph failure.
- [ ] Run the PIM tests; confirm they fail before the PIM service and Graph adapter operations exist.
- [ ] Define `PimStatus` values `active`, `eligible_inactive`, `not_eligible`, `activation_pending`, `approval_required`, `mfa_required`, `not_authorized`, `temporarily_unavailable` and `policy_blocked`, with role ID, display name, expiration, request ID, next step and safe Graph correlation ID.
- [ ] Implement `GET /api/users/{userObjectId}/pim` using the roles/PIM adapter and map Graph state/policy errors to the defined status model.
- [ ] Implement `POST /api/pim/activations` with `{ roleTemplateId, durationMinutes, justification }`, require `pim.activate`, require an idempotency key and require a confirmation flag from the UI. Reject unsupported Azure-resource/group PIM role types.
- [ ] Make `PimService` call Graph only after explicit confirmation and current eligibility verification. Return `activation_pending` or `active` only from the Graph response; never assume activation succeeded.
- [ ] For approval, MFA, Conditional Access, missing consent or unsupported Graph activation, return a guided handoff containing the exact next step, the relevant role, the tenant portal deep link where available and a refresh action. Do not claim the role is active.
- [ ] Implement the activation dialog with duration, justification, policy requirements, confirmation and accessible success/pending/error states. Disable the action when the role is not eligible or already active.
- [ ] Run API/frontend/E2E tests with deterministic PIM fixtures and document the real-test-tenant setup, cleanup and expected Graph permissions in `docs/testing/pim-test-matrix.md`.
- [ ] Commit as `feat: add explicit entra pim activation flow`.

## Task 12: Add audit events, error UX and observability safeguards

Make every meaningful platform action explainable and traceable without creating a secret-bearing log or pretending the platform audit is the Microsoft 365 audit source.

Files to create or update:

- `src/Api/Infrastructure/Persistence/Entities/AuditEvent.cs`
- `src/Api/Infrastructure/Observability/AuditWriter.cs`
- `src/Api/Infrastructure/Observability/CorrelationMiddleware.cs`
- `src/Api/Infrastructure/Observability/RedactingLogEnricher.cs`
- `src/Api/Features/Audit/AuditEndpoints.cs`
- `src/Api/Features/Shared/ProblemDetailsFactory.cs`
- `src/Web/src/components/ApiErrorState.tsx`
- `src/Web/src/features/audit/AuditPage.tsx`
- `tests/Api.UnitTests/Observability/AuditRedactionTests.cs`
- `tests/Api.UnitTests/Observability/ProblemDetailsFactoryTests.cs`
- `tests/Api.IntegrationTests/Audit/AuditEndpointTests.cs`
- `tests/Web.UnitTests/components/ApiErrorState.test.tsx`
- `tests/Web.E2E/audit-and-errors.spec.ts`
- `docs/operations/audit-and-incident-triage.md`

- [ ] Add failing audit tests for requested/succeeded/denied/failed/cancelled outcomes, safe before/after fields, Graph request ID, PIM request ID and correlation ID.
- [ ] Add failing redaction tests that feed representative token, secret, password, MFA, cookie and authorization-header strings into logs and assert they are removed or replaced with `[REDACTED]`.
- [ ] Run the tests; confirm they fail before the audit writer, redactor and problem-details factory exist.
- [ ] Define `AuditEvent` with workspace ID, tenant ID, actor tenant/object IDs, action, target type/id, outcome, timestamp, correlation ID, optional Graph request ID, optional PIM request ID, failure category and safe JSON metadata. Add indexes for workspace/time and target/time.
- [ ] Implement an audit writer that uses an outbox-like transaction boundary for platform intent/result metadata. Do not store raw request bodies for user creation or license/group actions.
- [ ] Add correlation middleware that accepts a safe correlation ID or creates one, propagates it to Graph request headers and returns it in the response.
- [ ] Implement RFC 7807-style problem responses for the error categories in the spec, including machine-readable `reasonCode`, `nextStep`, capability/PIM information and retry hints without raw Graph payloads.
- [ ] Implement the Audit activity page with workspace-scoped pagination, actor/action/outcome filters and a clear statement that Microsoft 365 audit logs remain authoritative for directory changes.
- [ ] Add App Insights-compatible structured logging with severity, operation name, workspace hash, tenant hash and correlation ID; never log tenant secrets or full user profiles.
- [ ] Run API/frontend/E2E tests and inspect emitted logs for forbidden values. Document retention, access and incident-triage rules in `docs/operations/audit-and-incident-triage.md`.
- [ ] Commit as `feat: add safe platform audit and error handling`.

## Task 13: Add overview, licenses and workspace settings completion screens

Complete the MVP navigation surfaces that make the hosted service operationally useful without drifting into the future dashboard or device scope.

Files to create or update:

- `src/Api/Features/Overview/OverviewEndpoints.cs`
- `src/Api/Features/Licenses/LicenseOverviewService.cs`
- `src/Api/Features/Licenses/LicenseEndpoints.cs`
- `src/Api/Features/Workspaces/WorkspaceSettingsEndpoints.cs`
- `src/Web/src/features/overview/OverviewPage.tsx`
- `src/Web/src/features/licenses/LicensesPage.tsx`
- `src/Web/src/features/workspace-settings/WorkspaceSettingsPage.tsx`
- `src/Web/src/features/workspace-settings/WorkspaceAdminForm.tsx`
- `tests/Api.UnitTests/Overview/OverviewServiceTests.cs`
- `tests/Api.IntegrationTests/Licenses/LicenseEndpointTests.cs`
- `tests/Api.IntegrationTests/Workspaces/WorkspaceSettingsEndpointTests.cs`
- `tests/Web.UnitTests/features/overview/OverviewPage.test.tsx`
- `tests/Web.UnitTests/features/licenses/LicensesPage.test.tsx`
- `tests/Web.UnitTests/features/workspace-settings/WorkspaceSettingsPage.test.tsx`
- `tests/Web.E2E/overview-and-settings.spec.ts`

- [ ] Add failing tests for overview freshness labels (`live`, `cached`, `stale`, `unavailable`), permission-health summaries, PIM attention, total-user count and license coverage.
- [ ] Add failing tests for license overview pagination and workspace settings authorization; a non-platform workspace member must receive read-only or hidden settings as defined by capability state.
- [ ] Run the tests; confirm they fail before the services and pages exist.
- [ ] Implement `GET /api/overview`, `GET /api/licenses`, and `GET/PATCH /api/workspaces/current/settings`. Overview totals may use bounded short-lived server cache, but the API must label the age/source and never use cached data to authorize an action.
- [ ] Implement the Licenses page with assigned/available counts, search/filter and safe links to affected users. Keep mutation controls on the user detail flow and capability-gate them.
- [ ] Implement Workspace settings for display name, enabled modules, default columns/filters, support instructions and default theme. Validate configuration against an allowlist; reject CSS, script, arbitrary navigation or authorization changes.
- [ ] Add page-level empty, stale, unavailable, no-permission and retry states and verify English strings come from the message catalog.
- [ ] Run all tests and the Playwright overview/settings spec at narrow and desktop viewports.
- [ ] Commit as `feat: complete operational overview and settings`.

## Task 14: Validate the real Entra test tenant and security boundaries

Exercise the actual delegated Graph, role, B2B and PIM paths before deployment. Keep real-tenant tests opt-in and never place credentials in the repository or automated CI logs.

Files to create or update:

- `tests/RealTenant/README.md`
- `tests/RealTenant/real-tenant.env.example`
- `tests/RealTenant/Scenarios/OnboardingScenario.cs`
- `tests/RealTenant/Scenarios/RoleCapabilityScenario.cs`
- `tests/RealTenant/Scenarios/PimScenario.cs`
- `tests/RealTenant/Scenarios/RevocationScenario.cs`
- `tests/Api.IntegrationTests/Security/CrossTenantAccessTests.cs`
- `tests/Api.IntegrationTests/Security/MutationReplayTests.cs`
- `tests/Web.E2E/security-boundaries.spec.ts`
- `docs/testing/test-tenant.md`
- `docs/security/threat-model.md`

- [ ] Add failing security tests for arbitrary tenant switching, cross-workspace user IDs, cross-tenant audit reads, unauthorized mutations and replayed idempotency keys.
- [ ] Run the security tests; confirm they fail until all route/resource checks from Tasks 2–12 are wired together.
- [ ] Configure a disposable or dedicated test tenant with separate customer-admin, Global Reader, User Administrator, eligible-inactive PIM and approval-required PIM accounts, plus an Atea B2B guest account. Record only object IDs and role assignments in the local secret store.
- [ ] Run the onboarding scenario: provision workspace, redeem invitation, perform customer-admin consent, verify connection, revoke consent and verify `consent_revoked`.
- [ ] Run the capability scenario: Global Reader sees permitted data but receives read-only mutation states; User Administrator performs approved lifecycle operations; missing role/consent is explained.
- [ ] Run the PIM scenario: eligible inactive role shows activation-required, successful activation changes the Graph-backed state, approval/MFA policy returns guided handoff, and no path silently activates.
- [ ] Run the B2B scenario: Atea guest access works only with customer-assigned active role and is denied after membership/role removal or session refresh.
- [ ] Run the throttling/transient-failure scenario using the fake transport and one controlled Graph integration check; verify bounded retry and safe retry messaging.
- [ ] Run Playwright security-boundary tests and inspect network traces to prove no Graph token or Graph hostname is exposed to the browser.
- [ ] Document threat assumptions, trust boundaries, test accounts, cleanup, test commands and evidence capture in `docs/security/threat-model.md` and `docs/testing/test-tenant.md`.
- [ ] Commit as `test: validate real tenant authorization and pim flows`.

## Task 15: Add Azure infrastructure-as-code and same-build deployment

Deploy the already validated application to Azure using the same containers and configuration contracts. Infrastructure is production-oriented but remains single-region for v1.

Files to create or update:

- `infra/main.bicep`
- `infra/modules/container-registry.bicep`
- `infra/modules/postgres.bicep`
- `infra/modules/key-vault.bicep`
- `infra/modules/container-apps.bicep`
- `infra/modules/monitoring.bicep`
- `infra/modules/identity.bicep`
- `infra/parameters/dev.json`, `infra/parameters/prod.example.json`
- `.github/workflows/validate-and-deploy.yml` or the selected Atea CI pipeline definition
- `docs/operations/azure-deployment.md`
- `README.md`

- [ ] Add an infrastructure validation test/script that asserts the Bicep parameter contract includes region, environment, image tag, PostgreSQL sizing, allowed ingress hostnames, Key Vault name, App Insights workspace and separate Entra app registration values.
- [ ] Run the validation script before adding modules; confirm it fails because the Bicep files and parameter contract do not exist.
- [ ] Define `main.bicep` modules for Azure Container Registry, Azure Database for PostgreSQL Flexible Server, Key Vault, Log Analytics/Application Insights, Container Apps environment and API/web container app. Use managed identity for secret access and private database networking where available in the selected region.
- [ ] Configure API secrets and connection strings through Key Vault references or managed identity; do not place secrets in container-app environment values, Bicep parameter files or CI logs.
- [ ] Configure controlled HTTPS ingress, managed certificates, health probes to `/health`, revision-based deployment and minimum/maximum replicas. Keep the region a parameter with an Atea-approved EU/EEA default.
- [ ] Configure the production Entra app registration redirect URIs and API audience separately from development; reject wildcard redirect URIs.
- [ ] Add deployment stages: build/test, dependency/security scan, container push with immutable tag, Bicep what-if, database migration job, deploy revision, smoke test and only then route customer traffic.
- [ ] Run `az bicep build`, `az deployment group what-if` against a non-production resource group, `docker compose build`, the full test suite and an Azure health smoke test. Do not claim production readiness until the real test-tenant scenarios pass against the deployed URL.
- [ ] Document provisioning prerequisites, secret rotation, rollback to the previous container revision, migration procedure, alert ownership, log retention and local-vs-Azure configuration mapping in `docs/operations/azure-deployment.md`.
- [ ] Commit as `ops: add azure deployment foundation`.

## Final Verification Checklist

Run these checks after Tasks 1–15 and attach the outputs to the implementation handoff.

- [ ] `dotnet test` passes for all API unit and integration projects.
- [ ] `npm run test -- --run` and `npm run build` pass for the frontend.
- [ ] Playwright passes for shell, overview, users, user detail, lifecycle, PIM, audit/errors, security and responsive viewport suites.
- [ ] `docker compose config` and `docker compose build` pass with no secret values in the rendered configuration.
- [ ] Cross-tenant, arbitrary-tenant-switching, unauthorized-mutation, token-leakage, session/cookie, CSRF, CSP/headers and mutation-replay checks pass.
- [ ] Global Reader, User Administrator, Atea B2B guest, missing-consent and PIM scenarios pass against the real test tenant.
- [ ] The browser makes no Graph requests and no Graph token is visible in storage, URLs, DOM, logs or error payloads.
- [ ] Every API endpoint is workspace-scoped and every mutation has capability, confirmation, idempotency and audit coverage.
- [ ] Atea light theme is the default, dark mode works, keyboard/focus/reduced-motion checks pass, and all visible v1 strings are English and externalized.
- [ ] Local and Azure builds use the same API/frontend container image contract and database migrations; only configuration and managed services differ.
- [ ] The final implementation is reviewed against the approved design spec, and any deferred device/dashboard work remains outside this MVP.
