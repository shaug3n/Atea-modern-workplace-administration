# Consent-First Customer Onboarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an Atea operator provision a customer workspace by tenant domain or GUID, then let an invited administrator grant the customer's SPA/API/Graph delegated permissions in one consent handoff before signing in and completing verified onboarding.

**Architecture:** Keep workspace provisioning, invitation eligibility, consent-state binding, membership, and Graph verification server-authoritative. Add a fixed-host OIDC tenant resolver, an anonymous nonce-limited invitation surface, versioned invitation consent challenges, atomic redemption/completion persistence, and an extracted delegated-scope probe; preserve GUID provisioning and authenticated legacy re-consent. Keep the invitation and callback pages public until sign-in is required, with transaction data confined to tab `sessionStorage`.

**Tech Stack:** .NET 10 minimal API, EF Core 10/Npgsql/PostgreSQL, React + TypeScript + Vite, MSAL Browser, xUnit/FluentAssertions, Vitest/jsdom, Python standard library `unittest`, Azure CLI (`az rest`), Bicep and GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-07-consent-first-onboarding-design.md`

## Global Constraints

- Request only `https://login.microsoftonline.com/{escaped-domain}/v2.0/.well-known/openid-configuration`. Disable redirects, set a five-second timeout and a 64-KiB response limit.
- Invitation challenge lifetime is **20 minutes or invitation expiry, whichever is earlier**.
- Limit scope-probe concurrency to four and bound comprehensive verification to 30 seconds; dispose leases and pass cancellation.
- Add named ASP.NET rate-limit policies to preview/start/resume: 20 requests/minute per trusted client IP across anonymous onboarding, with start additionally limited to five/minute/IP; no queue. Add a 200/minute/instance aggregate cap and `429` with `Retry-After`.
- Nonces retain the existing 43-character base64url, 256-bit-random format; signatures/hashes are never logged.
- Same-origin JSON POSTs for start/resume; reject cross-origin browser `Origin`, do not enable wildcard CORS, and accept no caller-controlled return URL. A bearer nonce is not a user identity. Preview GET is read-only.
- Hosted callback/public URLs require HTTPS, exact configured origin/path, no user-info/query/fragment, and registered redirect URIs. HTTP is limited to explicit localhost Development.
- Use the API's actual configured application-ID URI when constructing `{application-id-uri}/.default`; `api://{api-client-id}` is the default, not a browser-selected string.
- No app-only access, customer self-service signup, role assignment, background verification, email delivery, multi-cloud authority, or cross-origin copying of pending tab state.

## Review Focus

- A syntactically valid tenant domain whose discovery endpoint redirects or returns a mismatched issuer/endpoint must not resolve to an attacker-selected tenant; resolver tests cover redirect, authority, tenant-segment, and host mismatches.
- A callback's `tenant`, `admin_consent`, `scope`, or raw provider error cannot prove identity or consent; API tests assert wrong-tenant hints and denied callbacks neither authorize, consume invitation state, create membership, nor echo descriptions.
- Revocation/reissue between preview, redemption, and completion, plus a lost redemption response, must fail closed except for a still-valid challenge replay by the exact recorded redeemer; PostgreSQL tests exercise both races and recovery.
- A mix of definite missing scopes and transient/Conditional Access scope probes must return `permission_incomplete` plus separate `unknownScopes`, never claim full coverage or relabel unknown scopes as missing; verifier tests pin the mixed result.
- A cached MSAL account from another tenant and React StrictMode/double clicks must not select the wrong identity or duplicate redemption/completion; auth and browser-flow tests assert tenant-pinned selection and one request per persisted step.

---

## File Structure

The files below are the planned change surface. New test files are named in their owning tasks.

| File | Responsibility |
| --- | --- |
| `src/Api/Features/Workspaces/TenantResolver.cs` (new), `WorkspaceContracts.cs`, `WorkspaceEndpoints.cs`, `OnboardingOptions.cs` | Normalize and resolve tenant domains; extend both platform request contracts while keeping provisioning keyed by the canonical GUID. |
| `src/Api/Features/Workspaces/InvitationConsentService.cs` (new), `InvitationContracts.cs`, `InvitationService.cs`, `ConsentChallengeService.cs`, `OnboardingService.cs` | Invitation preview/start/resume, versioned invitation challenge parsing, redeemed-identity recovery, completion binding, truthful state transitions. |
| `src/Api/Infrastructure/Persistence/Entities/ConsentChallenge.cs`, `PlatformInvitation.cs`, `WorkplaceDbContext.cs`, `Repositories/IWorkspaceRepository.cs`, `Repositories/WorkspaceOnboardingRepository.cs`, `Migrations/20261007000100_AddConsentFirstOnboarding.cs` (new), `Migrations/WorkplaceDbContextModelSnapshot.cs` | Add nullable invitation/redeemer bindings, purpose defaults, restricted FK and lookup index; persist redemptions and atomically consume invitation challenges. |
| `src/Api/Infrastructure/Graph/DelegatedScopeAvailabilityReader.cs` (new), `IDelegatedScopeAvailabilityReader.cs` (new), `GraphAuthorizationSnapshotReader.cs`, `ConnectionHealthReader.cs`, `GraphScopeCatalog.cs`, `src/Api/Features/Workspaces/ConnectionVerificationService.cs` (new) | Extract uncached delegated-scope probes from the role snapshot and combine baseline plus optional scope coverage without conflating consent with role authority. |
| `src/Api/Program.cs` | Register resolver, consent/verifier services and named HTTP client; configure trusted forwarded headers and the anonymous onboarding rate-limit boundary. |
| `src/Web/src/features/admin/WorkspaceCreateForm.tsx`, `features/admin/adminApi.ts` | Accept a tenant domain or GUID and submit exactly the selected request field. |
| `src/Web/src/auth/AuthProvider.tsx`, `auth/msalConfig.ts`, `app/App.tsx`, `app/routes.tsx`, `main.tsx` | Render invitation/callback routes outside the sign-in guard while retaining MSAL initialization; pin sign-in, account selection and API token acquisition to the returned tenant. |
| `src/Web/src/features/invitations/InvitationLandingPage.tsx` (new), `features/invitations/pendingFlow.ts` (new), `features/invitations/invitationApi.ts`, `features/invitations/InvitationRedemptionPage.tsx`, `features/workspace-settings/ConsentCallbackPage.tsx`, `features/workspace-settings/OnboardingPage.tsx`, `messages/en.ts` | Implement the anonymous preview, persisted tab transaction, callback dispatch, automatic redemption/completion, retained member flow and legacy re-consent. |
| `infra/entra/delegated-permissions.json` (new), `infra/scripts/configure-entra-onboarding.py` (new), `infra/tests/test_configure_entra_onboarding.py` (new), `infra/tests/validate_contract.py`, `.env.example`, `docker-compose.yml`, `infra/application.bicep`, `infra/modules/container-apps.bicep`, `infra/parameters/application-dev.example.json`, `infra/parameters/application-prod.example.json`, `.github/workflows/validate-and-deploy.yml` | Check catalog/manifest parity; configure only the existing registrations with dry-run-first `az rest`; supply customer client, API URI, public authority and callback configuration without secrets. |
| `README.md`, `docs/testing/test-tenant.md`, `docs/operations/azure-deployment.md`, `docs/security/entra-app-registration.md`, `docs/security/threat-model.md`, `docs/security/data-isolation.md` | Document domain/GUID provisioning, registration linkage, rollout gate, one-link journey, anonymous nonce boundary, and deterministic versus live-tenant evidence. |

## Tasks

### Task 1: Resolve tenant domains from trusted OIDC metadata

**Files:**
- Create: `src/Api/Features/Workspaces/TenantResolver.cs`
- Modify: `src/Api/Program.cs:28-37,65-68` (register fixed-host discovery client and resolver)
- Test: `tests/Api.UnitTests/Workspaces/TenantResolverTests.cs` (create)

**Interfaces:**
- Produces: `ITenantResolver.ResolveAsync(string domain, CancellationToken cancellationToken = default) -> Task<TenantResolutionResult>`.
- Produces: `TenantResolutionResult(TenantResolutionStatus Status, Guid? TenantId)` where status is `Resolved`, `InvalidInput`, `NotFound`, or `Unavailable`.
- Consumes: `IHttpClientFactory` named client `EntraTenantDiscovery`; no provisioning/database interface.

- [x] **Step 1: Write failing resolver tests.**

```csharp
[Fact] public async Task ResolveAsync_uses_fixed_discovery_url_and_canonical_issuer()
{
    Assert.Equal(TenantResolutionStatus.Resolved, result.Status);
    Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.TenantId);
    Assert.Equal("https://login.microsoftonline.com/contoso.com/v2.0/.well-known/openid-configuration", handler.RequestUri!.AbsoluteUri);
}
[Fact] public async Task ResolveAsync_rejects_redirect_and_mismatched_metadata_authority()
{
    Assert.NotEqual(TenantResolutionStatus.Resolved, result.Status);
    Assert.Null(result.TenantId);
}
```

Also assert `common`, an IP address, URL syntax, ports, wildcards, and invalid IDN input return `InvalidInput`; timeout returns `Unavailable`, a 404 returns `NotFound`, and successful aliases are cached while failures are not.

- [x] **Step 2: Run the focused tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~TenantResolverTests`
Expected: FAIL because the resolver types and fixed-metadata validation do not exist.

- [x] **Step 3: Implement `OidcTenantResolver.ResolveAsync` in `TenantResolver.cs`.**

Normalize trimmed/lowercase DNS and IDN names to ASCII; issue only the fixed HTTPS discovery request with redirects disabled, a five-second timeout and a 64-KiB body cap. Require a GUID issuer segment ending `/v2.0` and authorization/token endpoints on the same trusted host and tenant. Cache successful mappings for one hour only.

- [x] **Step 4: Re-run the focused resolver tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~TenantResolverTests`
Expected: PASS, including all fake-handler cases with no network access.

- [x] **Step 5: Commit.**

```bash
git add src/Api/Features/Workspaces/TenantResolver.cs src/Api/Program.cs tests/Api.UnitTests/Workspaces/TenantResolverTests.cs
git commit -m "feat: resolve customer tenant domains"
```

### Task 2: Accept domain or GUID in platform provisioning

**Files:**
- Modify: `src/Api/Features/Workspaces/WorkspaceContracts.cs:1-5`
- Modify: `src/Api/Features/Workspaces/WorkspaceEndpoints.cs:12-23,70-102`
- Test: `tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs:69-117` and create `WorkspaceTenantInputEndpointTests.cs`

**Interfaces:**
- Consumes: `ITenantResolver.ResolveAsync(string domain, CancellationToken) -> Task<TenantResolutionResult>`.
- Produces: `CreateWorkspaceRequest(Guid? TenantId, string? TenantDomain, string DisplayName)` and `OnboardWorkspaceRequest(Guid? TenantId, string? TenantDomain, string? DisplayName, string? AdminUpn, string? AdminDisplayName = null)`.
- Produces: both handlers resolve input before calling the existing `IWorkspaceProvisioningService.CreateWorkspaceAsync(Guid tenantId, string displayName, PlatformOperatorIdentity? operatorIdentity, CancellationToken)` or `OnboardAsync(Guid tenantId, string displayName, string adminUpn, string adminDisplayName, AuditEvent auditEvent, PlatformOperatorIdentity? operatorIdentity, CancellationToken)`.

- [x] **Step 1: Add failing endpoint tests.**

```csharp
[Fact] public async Task Create_rejects_missing_or_mutually_supplied_tenant_inputs()
{
    Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    Assert.Contains("\"error\":\"invalid_tenant_input\"", await missing.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
}
[Fact] public async Task Onboard_resolves_domain_before_provisioning_and_returns_canonical_tenant()
{
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal(TenantId, provisioning.LastTenantId);
    Assert.Equal(0, provisioning.CreateCalls);
}
```

Cover GUID-only requests unchanged, `Guid.Empty`, malformed domain (`400 invalid_tenant_input`), valid domain not found (`422 tenant_domain_not_found`), timeout (`503 tenant_resolution_unavailable`), and no provisioning call when discovery fails.

- [x] **Step 2: Run the endpoint tests.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~WorkspaceTenantInputEndpointTests`
Expected: FAIL because requests have no `tenantDomain` contract or resolver injection.

- [x] **Step 3: Implement the two request records and resolve before provisioning.**

In `WorkspaceEndpoints.cs`, require exactly one nonempty tenant input, map `InvalidInput` to `400 invalid_tenant_input`, `NotFound` to `422 tenant_domain_not_found`, and `Unavailable` to `503 tenant_resolution_unavailable`. Continue passing only the resolved GUID to provisioning so duplicate checks remain canonical.

- [x] **Step 4: Run new and retained platform provisioning tests.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~WorkspaceTenantInputEndpointTests|FullyQualifiedName~WorkspaceEndpointsTests"`
Expected: PASS; the existing authenticated GUID create/onboard assertions remain unchanged.

- [x] **Step 5: Commit.**

```bash
git add src/Api/Features/Workspaces/WorkspaceContracts.cs src/Api/Features/Workspaces/WorkspaceEndpoints.cs tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs tests/Api.IntegrationTests/Workspaces/WorkspaceTenantInputEndpointTests.cs
git commit -m "feat: provision workspaces by tenant domain"
```

### Task 3: Submit the selected tenant input from the admin form

**Files:**
- Modify: `src/Web/src/features/admin/WorkspaceCreateForm.tsx:3-27`
- Modify: `src/Web/src/features/admin/adminApi.ts:67-73`
- Test: create `tests/Web.UnitTests/features/admin/WorkspaceCreateForm.test.tsx`; update `tests/Web.E2E/admin-onboarding.spec.tsx:41-102`

**Interfaces:**
- Produces: `WorkspaceCreateInput = { tenantId: string; tenantDomain?: never } | { tenantId?: never; tenantDomain: string }`, combined with `displayName`, `adminUpn`, and optional `adminDisplayName`.
- Produces: `adminApi.onboardWorkspace(input: WorkspaceCreateInput & { displayName: string; adminUpn: string; adminDisplayName?: string }): Promise<WorkspaceOnboardingResult>`.

- [ ] **Step 1: Write failing form tests.**

```tsx
it('submits a tenant domain as tenantDomain, not tenantId', async () => {
  fireEvent.change(screen.getByLabelText('Tenant domain or ID'), { target: { value: 'contoso.com' } });
  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tenantDomain: 'contoso.com' }));
  expect(onSubmit.mock.calls[0][0]).not.toHaveProperty('tenantId');
});
it('keeps a GUID in tenantId', async () => {
  expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tenantId: '11111111-1111-1111-1111-111111111111' }));
});
```

- [ ] **Step 2: Run the focused Vitest test.**

Run: `npm run test:behavior --prefix src/Web -- --run WorkspaceCreateForm`
Expected: FAIL because the current form rejects domains and labels the field `Tenant ID`.

- [ ] **Step 3: Update the form and API client.**

Use the exact label **“Tenant domain or ID”**. Classify only a GUID-shaped value into `tenantId`; submit any other nonempty value as `tenantDomain` for server validation. Keep display-name and first-admin validation and do not infer a tenant from the email.

- [ ] **Step 4: Run form and onboarding browser tests.**

Run: `npm run test:behavior --prefix src/Web -- --run WorkspaceCreateForm`
Run: `npm test --prefix tests/Web.E2E -- admin-onboarding`
Expected: PASS; the GUID fixture sends the legacy JSON shape and the domain fixture sends only `tenantDomain`.

- [ ] **Step 5: Commit.**

```bash
git add src/Web/src/features/admin/WorkspaceCreateForm.tsx src/Web/src/features/admin/adminApi.ts tests/Web.UnitTests/features/admin/WorkspaceCreateForm.test.tsx tests/Web.E2E/admin-onboarding.spec.tsx
git commit -m "feat(web): accept tenant domains in onboarding"
```

### Task 4: Persist invitation-bound consent and redeemer identity

**Files:**
- Modify: `src/Api/Infrastructure/Persistence/Entities/ConsentChallenge.cs`, `Entities/PlatformInvitation.cs`, `WorkplaceDbContext.cs:58-70,107-116`
- Modify: `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs:15-29`, `Repositories/WorkspaceOnboardingRepository.cs:1-126`
- Create: `src/Api/Infrastructure/Persistence/Migrations/20261007000100_AddConsentFirstOnboarding.cs`
- Modify: `src/Api/Infrastructure/Persistence/Migrations/WorkplaceDbContextModelSnapshot.cs:102-145` and the `PlatformInvitation` model block
- Test: create `tests/Api.IntegrationTests/Persistence/ConsentFirstOnboardingMigrationTests.cs`

**Interfaces:**
- Produces: `ConsentChallenge.InvitationId: Guid?` and `Purpose: string` defaulting to `"workspace"`.
- Produces: `PlatformInvitation.RedeemedByTenantObjectId: Guid?`.
- Consumes: current `IInvitationRepository.RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken)` and the current hash-primary-key `ConsentChallenges` model.
- Produces: a restricted-delete `ConsentChallenge.InvitationId -> PlatformInvitation.Id` FK and index `(InvitationId, ExpiresAt)`; preserve the unique `StateHash` primary key.

- [ ] **Step 1: Write failing PostgreSQL migration and repository tests.**

```csharp
[Fact] public async Task Migration_defaults_old_challenges_and_keeps_new_bindings_nullable()
{
    Assert.Equal("workspace", saved.Purpose);
    Assert.Null(saved.InvitationId);
    Assert.Null(invitation.RedeemedByTenantObjectId);
}
[Fact] public async Task RedeemAsync_records_object_id_in_the_membership_transaction()
{
    Assert.Equal(ObjectId, redeemedInvitation.RedeemedByTenantObjectId);
}
```

Migrate a database to `20260926000100_AddPlatformWorkspaceGrants`, insert legacy records, then migrate to the new migration and assert old rows are retained with the default/null values.

- [ ] **Step 2: Run the migration tests.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~ConsentFirstOnboardingMigrationTests`
Expected: FAIL because the new columns, FK, and migration do not exist.

- [ ] **Step 3: Add the additive migration and model mapping.**

Add nullable `InvitationId` with `DeleteBehavior.Restrict`, `Purpose` with default `"workspace"`, index invitation plus expiry, and nullable redeemer object ID. Update the model snapshot without changing existing invitation/challenge keys.

- [ ] **Step 4: Persist redeemer identity in the existing atomic redemption transaction.**

In `WorkspaceOnboardingRepository.RedeemAsync`, set `RedeemedByTenantObjectId` in the same transaction as `RedeemedAt` and membership. Change connection state to `consent_required` only when the workspace is `awaiting_invitation`; a later invitation must not reset a recorded connection.

- [ ] **Step 5: Run the persistence tests and commit.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~ConsentFirstOnboardingMigrationTests`
Expected: PASS against PostgreSQL; prior rows survive and redeemer identity is committed atomically.

```bash
git add src/Api/Infrastructure/Persistence/Entities/ConsentChallenge.cs src/Api/Infrastructure/Persistence/Entities/PlatformInvitation.cs src/Api/Infrastructure/Persistence/WorkplaceDbContext.cs src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs src/Api/Infrastructure/Persistence/Repositories/WorkspaceOnboardingRepository.cs src/Api/Infrastructure/Persistence/Migrations/20261007000100_AddConsentFirstOnboarding.cs src/Api/Infrastructure/Persistence/Migrations/WorkplaceDbContextModelSnapshot.cs tests/Api.IntegrationTests/Persistence/ConsentFirstOnboardingMigrationTests.cs
git commit -m "feat: persist invitation consent bindings"
```

### Task 5: Version consent challenges without breaking legacy state

**Files:**
- Modify: `src/Api/Features/Workspaces/ConsentChallengeService.cs:1-80`
- Test: `tests/Api.UnitTests/Workspaces/ConsentChallengeTests.cs:1-85`

**Interfaces:**
- Preserves: `Create(Guid workspaceId, Guid tenantId)`, `Create(Guid workspaceId, Guid tenantId, DateTimeOffset expiresAt)`, `Validate(...)`, and `TryRead(...)` for five-field legacy workspace challenges.
- Produces: `CreateInvitation(Guid workspaceId, Guid tenantId, Guid invitationId, DateTimeOffset invitationExpiresAt) -> ConsentChallenge` and `TryReadInvitation(string challenge, Guid expectedWorkspaceId, Guid expectedTenantId, Guid expectedInvitationId, out ConsentChallengePayload payload) -> bool`.
- Produces: verified invitation payload fields `Purpose`, `CorrelationId`, `WorkspaceId`, `TenantId`, `InvitationId`, `ExpiresAt`; HMAC validation precedes field use/database access.

- [ ] **Step 1: Add failing challenge-format tests.**

```csharp
[Fact] public void Invitation_challenge_expires_at_twenty_minutes_or_invitation_expiry()
{
    Assert.Equal(DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds(), challenge.ExpiresAt.ToUnixTimeSeconds());
    Assert.Equal(invitationExpiresAt, shortChallenge.ExpiresAt);
}
[Fact] public void Legacy_challenges_remain_valid_but_wrong_invitation_binding_is_rejected()
{
    Assert.True(service.TryRead(legacy.Challenge, workspaceId, tenantId, out _));
    Assert.False(service.TryReadInvitation(invitation.Challenge, workspaceId, tenantId, Guid.NewGuid(), out _));
}
```

Also assert tampered signature, unsupported purpose/version, malformed timestamp, oversized challenge, expired challenge, and wrong workspace/tenant/invitation are rejected with no repository call.

- [ ] **Step 2: Run the unit tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~ConsentChallengeTests`
Expected: FAIL because invitation-purpose creation and parsing are missing.

- [ ] **Step 3: Implement invitation challenge creation/parsing in `ConsentChallengeService`.**

Retain the legacy five-field payload and ten-minute expiry. Sign the versioned invitation fields with the existing HMAC-SHA256 key, use fixed-time signature comparison, strict parsing, and no raw nonce/email/claims. Clamp invitation expiry to `min(now + 20 minutes, invitation.ExpiresAt)` and keep state persistence hash-only.

- [ ] **Step 4: Run consent challenge tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~ConsentChallengeTests`
Expected: PASS for both state formats, including tamper, expiry, size and cross-binding cases.

- [ ] **Step 5: Commit.**

```bash
git add src/Api/Features/Workspaces/ConsentChallengeService.cs tests/Api.UnitTests/Workspaces/ConsentChallengeTests.cs
git commit -m "feat: add versioned invitation consent challenges"
```

### Task 6: Add the protected anonymous invitation API

**Files:**
- Create: `src/Api/Features/Workspaces/InvitationConsentService.cs`
- Modify: `src/Api/Features/Workspaces/InvitationContracts.cs`, `InvitationService.cs:1-58`, `WorkspaceEndpoints.cs:12-31,153-225`, `OnboardingOptions.cs:1-63`, `Program.cs:28-68,138-147`
- Modify: `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs:24-29`, `Repositories/WorkspaceOnboardingRepository.cs:1-84`
- Test: create `tests/Api.UnitTests/Workspaces/InvitationConsentServiceTests.cs` and `tests/Api.IntegrationTests/Workspaces/InvitationConsentEndpointTests.cs`

**Interfaces:**
- Produces: `IInvitationReadRepository.FindByNonceHashAsync(string nonceHash, CancellationToken cancellationToken = default) -> Task<InvitationLookup?>`, where `InvitationLookup` contains only server-side invitation ID, workspace ID/name, tenant ID, role, expiry, redeemed/revoked status and invitation expiry.
- Produces: `IInvitationConsentService.PreviewAsync(string nonce, CancellationToken) -> Task<InvitationPreviewResult?>`, `StartAsync(string nonce, CancellationToken) -> Task<InvitationConsentStartResult>`, and `ResumeAsync(string nonce, string state, Guid? tenant, string? errorCode, CancellationToken) -> Task<InvitationConsentResumeResult>`.
- Produces API contracts: preview `{ workspaceName, expiresAt, flow, permissionScopes }`; start `{ authorizationUrl, scopes, challenge, correlationId, expiresAt }`; resume `{ valid, status, tenantId?, correlationId }`.
- Consumes: the versioned challenge methods from Task 5, `GraphScopeCatalog.CapabilityEvaluationScopes`, and validated `OnboardingOptions.CustomerClientId`, `ApiApplicationIdUri`, and exact `ConsentRedirectUri`.

- [ ] **Step 1: Write failing service and endpoint tests.**

```csharp
[Fact] public async Task Preview_returns_only_the_safe_consent_first_shape()
{
    Assert.Equal(new[] { "workspaceName", "expiresAt", "flow", "permissionScopes" }, propertyNames);
    Assert.Equal("consent_first", body.RootElement.GetProperty("flow").GetString());
    Assert.False(body.RootElement.TryGetProperty("tenantId", out _));
}
[Fact] public async Task Start_targets_customer_spa_and_api_application_uri_default()
{
    Assert.Equal("spa-client", query["client_id"]);
    Assert.Equal("api://customer-api/.default", query["scope"]);
}
```

Cover member invitations (`flow == "sign_in"` and consent start `409 invitation_consent_not_available`), customer-admin/workspace-owner eligibility, identical `404 invitation_unavailable` for malformed/unknown/expired/revoked/redeemed nonces, safe `503` persistence/configuration failure, and resume `ready_to_sign_in` without consuming the challenge. Assert no raw invitee email, tenant/workspace/invitation IDs, module assignment or diagnostics in anonymous responses.

- [ ] **Step 2: Run the focused API tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~InvitationConsentServiceTests`
Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~InvitationConsentEndpointTests`
Expected: FAIL because no anonymous preview/start/resume route or service exists.

- [ ] **Step 3: Implement invitation read, preview, start and resume boundaries.**

Hash the 43-character nonce before repository access. Persist only `SHA-256(state)` and server bindings. Build the tenant-specific admin-consent URL with the customer SPA client ID and `scope={ApiApplicationIdUri}/.default`; never use Graph `.default` for this new SPA-targeted flow. `ResumeAsync` validates signed payload, state hash, invitation/workspace/tenant binding, expiry and lifecycle without consuming state or changing onboarding status.

- [ ] **Step 4: Map only these routes as anonymous and apply the onboarding request protections.**

Add explicit `.AllowAnonymous()` to `GET /api/invitations/{nonce}/preview` and POST start/resume only; retain `.RequireAuthorization()` on redemption, workspace, and platform routes. Apply 20/minute/IP common and 5/minute/IP start policies, a 200/minute/instance cap, no queue, and `429` with `Retry-After`. Trust forwarded client IPs only from configured proxy addresses; never use arbitrary `X-Forwarded-For`. Require JSON and same-origin `Origin` for POSTs, reject wildcard CORS, cap callback body at 8 KiB and state at 4 KiB, and reject invalid nonce length before hashing. Add `Cache-Control: no-store` and `Referrer-Policy: no-referrer` on invitation API and public landing/callback responses; ensure application logs, audits and errors omit raw invitation paths, callback query strings, state and consent URLs.

- [ ] **Step 5: Run focused API tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~InvitationConsentServiceTests`
Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~InvitationConsentEndpointTests`
Expected: PASS; tests verify anonymous access is limited to the three explicit endpoints, `Origin` rejection, no-store headers, 20/5/200 limits, trusted-proxy behavior and `Retry-After`.

- [ ] **Step 6: Commit.**

```bash
git add src/Api/Features/Workspaces/InvitationConsentService.cs src/Api/Features/Workspaces/InvitationContracts.cs src/Api/Features/Workspaces/InvitationService.cs src/Api/Features/Workspaces/WorkspaceEndpoints.cs src/Api/Features/Workspaces/OnboardingOptions.cs src/Api/Program.cs src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs src/Api/Infrastructure/Persistence/Repositories/WorkspaceOnboardingRepository.cs tests/Api.UnitTests/Workspaces/InvitationConsentServiceTests.cs tests/Api.IntegrationTests/Workspaces/InvitationConsentEndpointTests.cs
git commit -m "feat: add protected anonymous invitation consent"
```

### Task 7: Bind redemption and authenticated completion to the redeemer

**Files:**
- Modify: `src/Api/Features/Workspaces/WorkspaceEndpoints.cs:30,153-163,213-225`
- Modify: `src/Api/Features/Workspaces/InvitationConsentService.cs`, `ConsentChallengeService.cs`, `src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs:24-29`, `Repositories/WorkspaceOnboardingRepository.cs:84-126`
- Modify: `src/Api/Features/Workspaces/InvitationContracts.cs`
- Test: `tests/Api.IntegrationTests/Workspaces/InvitationRedemptionEndpointTests.cs:28-228`; create `tests/Api.IntegrationTests/Workspaces/InvitationConsentRaceTests.cs`

**Interfaces:**
- Produces: `InvitationRedemptionRequest(string? Challenge = null)` as an optional `/redeem` body.
- Produces: `IInvitationRepository.RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, string? invitationStateHash, CancellationToken cancellationToken = default) -> Task<InvitationRedemption?>`.
- Produces: `IConsentChallengeRepository.TryConsumeInvitationAsync(string stateHash, Guid invitationId, Guid workspaceId, Guid tenantId, Guid redeemerObjectId, DateTimeOffset now, CancellationToken cancellationToken = default) -> Task<bool>`.
- Produces: `IInvitationConsentService.CompleteInvitationAsync(string state, Guid workspaceId, Guid tokenTenantId, Guid tokenObjectId, Guid? callbackTenant, string? errorCode, CancellationToken) -> Task<InvitationCompletionResult>`.

- [ ] **Step 1: Add failing redemption and completion tests.**

```csharp
[Fact] public async Task Challenge_bound_replay_recovers_only_for_the_recorded_redeemer()
{
    Assert.Equal(HttpStatusCode.OK, sameIdentity.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, nonceOnlyReplay.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, anotherIdentity.StatusCode);
    Assert.Equal(1, repository.RedemptionAuditCount);
}
[Fact] public async Task Wrong_callback_tenant_or_identity_does_not_consume_invitation_state()
{
    Assert.Contains("\"valid\":false", body);
    Assert.Equal(0, repository.InvitationConsumeCalls);
}
```

Also cover callback with no matching redeemed invitation (membership alone is insufficient), concurrent completion (exactly one consume), invalid/expired/tampered state, revoked/reissued invitation race, and `resume` after redemption while its own challenge is still valid.

- [ ] **Step 2: Run the focused integration tests.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~InvitationRedemptionEndpointTests|FullyQualifiedName~InvitationConsentRaceTests"`
Expected: FAIL because redeemer identity is not currently recorded or challenge-bound replay is not supported.

- [ ] **Step 3: Implement challenge-bound recovery in the redemption transaction.**

Validate the optional challenge signature/purpose and invitation binding before passing its hash to persistence. Keep nonce-only replay invalid. In one transaction, persist the first redeemer object ID and membership; return the previous safe success result only for the same recorded object ID with the same live invitation challenge. Do not add a second membership or audit event. A different identity always fails.

- [ ] **Step 4: Implement authenticated invitation completion and transactional consume.**

Require token tenant/workspace, invitation ID, recorded redeemer object ID, unrevoked/unexpired invitation, unexpired challenge and callback hint agreement. Enforce all predicates in the atomic consume operation. Denied invitation callbacks do not consume or mutate state; preserve the legacy challenge's existing denial-consumes behavior.

- [ ] **Step 5: Run invitation API and PostgreSQL race tests.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~InvitationRedemptionEndpointTests|FullyQualifiedName~InvitationConsentRaceTests"`
Expected: PASS; simultaneous completion starts at most one verifier path and revoke/reissue races fail closed.

- [ ] **Step 6: Commit.**

```bash
git add src/Api/Features/Workspaces/WorkspaceEndpoints.cs src/Api/Features/Workspaces/InvitationConsentService.cs src/Api/Features/Workspaces/ConsentChallengeService.cs src/Api/Features/Workspaces/InvitationContracts.cs src/Api/Infrastructure/Persistence/Repositories/IWorkspaceRepository.cs src/Api/Infrastructure/Persistence/Repositories/WorkspaceOnboardingRepository.cs tests/Api.IntegrationTests/Workspaces/InvitationRedemptionEndpointTests.cs tests/Api.IntegrationTests/Workspaces/InvitationConsentRaceTests.cs
git commit -m "fix: bind invitation recovery to redeemer identity"
```

### Task 8: Extract uncached delegated scope availability probing

**Files:**
- Create: `src/Api/Infrastructure/Graph/IDelegatedScopeAvailabilityReader.cs`, `DelegatedScopeAvailabilityReader.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphAuthorizationSnapshotReader.cs:1-101`, `GraphScopeCatalog.cs:1-56`, `src/Api/Program.cs:59-68`
- Test: create `tests/Api.UnitTests/Graph/DelegatedScopeAvailabilityReaderTests.cs`

**Interfaces:**
- Produces: `IDelegatedScopeAvailabilityReader.ReadAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) -> Task<IReadOnlyCollection<DelegatedScopeResult>>`.
- Produces: `DelegatedScopeResult(string Scope, ScopeAvailability Status, string? ProblemCategory)` with `Available`, `MissingConsent`, or `Unknown`.
- Consumes: `IDelegatedGraphClientFactory.CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken)`; the scope reader must not call role/assignment endpoints.

- [ ] **Step 1: Write failing extraction and classification tests.**

```csharp
[Fact] public async Task Classifies_only_positive_missing_consent_as_missing()
{
    Assert.Equal(ScopeAvailability.MissingConsent, results["User.Read.All"].Status);
    Assert.Equal(ScopeAvailability.Unknown, results["Group.Read.All"].Status);
    Assert.Equal(ScopeAvailability.Available, results["User.Read"].Status);
}
[Fact] public async Task Never_runs_more_than_four_scope_probes_concurrently()
{
    Assert.InRange(factory.MaximumActiveCalls, 1, 4);
}
```

Also assert caller cancellation propagates, leases are disposed, unknown token/Conditional Access/MFA and temporary errors remain `Unknown`, and the extracted reader issues no role read.

- [ ] **Step 2: Run the focused graph unit tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~DelegatedScopeAvailabilityReaderTests`
Expected: FAIL because scope probing is private to `GraphAuthorizationSnapshotReader`.

- [ ] **Step 3: Extract and register the reader.**

Move the per-scope OBO probe into `DelegatedGraphScopeAvailabilityReader`; classify only the token mapper's positive consent-required outcome as `MissingConsent`, all unclassified/temporary/Conditional Access outcomes as `Unknown`. Limit concurrency to four and pass cancellation through each lease.

- [ ] **Step 4: Reuse the reader in the authorization snapshot.**

Inject `IDelegatedScopeAvailabilityReader` into `GraphAuthorizationSnapshotReader`; preserve its authorization-role read boundary and 15-second snapshot cache, using reader results only to populate current scope availability.

- [ ] **Step 5: Run graph tests and commit.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~DelegatedScopeAvailabilityReaderTests|FullyQualifiedName~GraphAuthorizationSnapshotReaderTests"`
Expected: PASS; authorization snapshot behavior remains unchanged and scope-probe guarantees hold.

```bash
git add src/Api/Infrastructure/Graph/IDelegatedScopeAvailabilityReader.cs src/Api/Infrastructure/Graph/DelegatedScopeAvailabilityReader.cs src/Api/Infrastructure/Graph/GraphAuthorizationSnapshotReader.cs src/Api/Infrastructure/Graph/GraphScopeCatalog.cs src/Api/Program.cs tests/Api.UnitTests/Graph/DelegatedScopeAvailabilityReaderTests.cs
git commit -m "refactor: extract delegated scope availability probes"
```

### Task 9: Verify full delegated coverage and expose retry semantics

**Files:**
- Create: `src/Api/Features/Workspaces/ConnectionVerificationService.cs`
- Modify: `src/Api/Features/Workspaces/OnboardingService.cs:5-67`, `WorkspaceContracts.cs:1-23`, `WorkspaceEndpoints.cs:26-29,165-225`
- Modify: `src/Api/Infrastructure/Graph/IConnectionHealthReader.cs:1-25`, `ConnectionHealthReader.cs`
- Test: `tests/Api.UnitTests/Workspaces/OnboardingStateTests.cs:1-33`; create `tests/Api.UnitTests/Workspaces/ConnectionVerificationServiceTests.cs`; update `tests/Api.IntegrationTests/Workspaces/ConsentCallbackEndpointTests.cs:26-195`

**Interfaces:**
- Produces: `IWorkspaceConnectionVerifier.VerifyAsync(WorkspaceContext context, bool includePermissionCoverage, CancellationToken cancellationToken = default) -> Task<WorkspaceConnectionVerification>`.
- Produces: `PermissionCoverage(IReadOnlyCollection<string> AvailableScopes, IReadOnlyCollection<string> MissingScopes, IReadOnlyCollection<string> UnknownScopes)`; `ConnectionHealthDto` adds nullable `PermissionCoverage`.
- Produces: `ConnectionHealthCheckRequest(bool IncludePermissionCoverage = false)`.
- Preserves: default baseline-only `POST /api/workspaces/current/connection-health/check`; optional `{ "includePermissionCoverage": true }` invokes the comprehensive verifier.

- [ ] **Step 1: Add failing verifier and transition tests.**

```csharp
[Fact] public async Task Definite_missing_and_unknown_scopes_remain_distinct()
{
    Assert.Equal(ConnectionState.PermissionIncomplete, result.Health.Status);
    Assert.Equal(new[] { "User.Read.All" }, result.PermissionCoverage.MissingScopes);
    Assert.Equal(new[] { "Group.Read.All" }, result.PermissionCoverage.UnknownScopes);
}
[Theory] public void Post_redemption_state_can_transition_to_each_verification_result(string prior, string result)
{
    Assert.True(OnboardingStateMachine.CanTransition(prior, result));
}
```

Cover all post-redemption states transitioning to `consent_required`, `connected`, `permission_incomplete`, `temporarily_unavailable`, `consent_revoked`, and `connection_failed`; assert `awaiting_invitation -> connected` remains false. Assert all catalog scopes available yields `connected`, unknown-only probes yield `temporarily_unavailable`, baseline failure preserves its category, and `LastVerifiedAt` is set only after an observed check.

- [ ] **Step 2: Run the focused API tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~ConnectionVerificationServiceTests|FullyQualifiedName~OnboardingStateTests"`
Expected: FAIL because the verifier and comprehensive transition results are missing.

- [ ] **Step 3: Implement `IWorkspaceConnectionVerifier.VerifyAsync`.**

Use `IConnectionHealthReader` for baseline `/me`; for comprehensive mode probe every `GraphScopeCatalog.CapabilityEvaluationScopes` value uncached, with a 30-second total bound, concurrency four, cancellation and disposal. Missing scopes force `permission_incomplete` even if other probes are unknown; unknown-only coverage is `temporarily_unavailable`. Persist safe status, available scope names, failure category and observed verification time only.

- [ ] **Step 4: Extend invitation completion and connection-check endpoints.**

After Task 7 has atomically consumed a valid authenticated invitation state, run comprehensive verification exactly once and return the existing `valid/status/correlationId` plus invitation-only `health` and `permissionCoverage`. Keep legacy completion fields/status and its separate baseline check. Invalid state runs no Graph call. Extend the health-check endpoint with `includePermissionCoverage`; baseline-only remains default.

- [ ] **Step 5: Run focused API tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~ConnectionVerificationServiceTests|FullyQualifiedName~OnboardingStateTests"`
Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~ConsentCallbackEndpointTests|FullyQualifiedName~OnboardingEndpointTests"`
Expected: PASS; legacy response/status assertions remain valid and invitation coverage is truthful.

- [ ] **Step 6: Commit.**

```bash
git add src/Api/Features/Workspaces/ConnectionVerificationService.cs src/Api/Features/Workspaces/OnboardingService.cs src/Api/Features/Workspaces/WorkspaceContracts.cs src/Api/Features/Workspaces/WorkspaceEndpoints.cs src/Api/Infrastructure/Graph/IConnectionHealthReader.cs src/Api/Infrastructure/Graph/ConnectionHealthReader.cs tests/Api.UnitTests/Workspaces/OnboardingStateTests.cs tests/Api.UnitTests/Workspaces/ConnectionVerificationServiceTests.cs tests/Api.IntegrationTests/Workspaces/ConsentCallbackEndpointTests.cs
git commit -m "feat: verify delegated permission coverage"
```

### Task 10: Check the reviewed Graph delegated-permission manifest

**Files:**
- Create: `infra/entra/delegated-permissions.json`
- Create: `tests/Api.UnitTests/Graph/DelegatedPermissionManifestTests.cs`
- Read-only source: `src/Api/Infrastructure/Graph/GraphScopeCatalog.cs:3-56`

**Interfaces:**
- Produces: JSON manifest with Graph application ID `00000003-0000-0000-c000-000000000000`, one Graph `requiredResourceAccess` block, permission type `"Scope"`, and distinct ordinal-sorted scope names from `GraphScopeCatalog.CapabilityEvaluationScopes`.
- Consumes: every operation-specific Graph scope group in `GraphScopeCatalog`; no environment tenant/client IDs, secrets, or tokens.

- [ ] **Step 1: Write the failing manifest parity test.**

```csharp
[Fact] public void Manifest_matches_catalog_with_only_graph_delegated_scopes()
{
    Assert.Equal(GraphScopeCatalog.CapabilityEvaluationScopes.Distinct().Order(), manifest.ScopeNames);
    Assert.All(manifest.Permissions, permission => Assert.Equal("Scope", permission.Type));
    Assert.All(operationScopes, scope => Assert.Contains(scope, manifest.ScopeNames));
}
```

- [ ] **Step 2: Run the test.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~DelegatedPermissionManifestTests`
Expected: FAIL because the manifest does not exist.

- [ ] **Step 3: Add the catalog-derived manifest and test.**

Keep permission names distinct and sorted; assert every scope in the operation-specific groups is covered by the manifest and the manifest has no application-permission entries or environment identifiers.

- [ ] **Step 4: Run the manifest test.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~DelegatedPermissionManifestTests`
Expected: PASS; changing the catalog without reviewing/updating the manifest fails CI.

- [ ] **Step 5: Commit.**

```bash
git add infra/entra/delegated-permissions.json tests/Api.UnitTests/Graph/DelegatedPermissionManifestTests.cs
git commit -m "feat: track reviewed Graph delegated permissions"
```

### Task 11: Wire consent-first configuration through local and hosted environments

**Files:**
- Modify: `src/Api/Features/Workspaces/OnboardingOptions.cs:1-63`, `src/Api/Program.cs:28-37`
- Modify: `.env.example:4-10,22-24`, `docker-compose.yml:1-47`
- Modify: `infra/application.bicep:15-58`, `infra/modules/container-apps.bicep:200-280`, `infra/parameters/application-dev.example.json:14-29`, `infra/parameters/application-prod.example.json:14-29`
- Modify: `.github/workflows/validate-and-deploy.yml:30-110`, `infra/tests/validate_contract.py:48-69,145-180,192-207`
- Test: `tests/Api.UnitTests/Workspaces/OnboardingOptionsTests.cs:10-104`; infrastructure contract check

**Interfaces:**
- Produces: `OnboardingOptions.CustomerClientId`, `ApiApplicationIdUri`, and `TrustedProxyAddresses`; validates anonymous invitation start is enabled only when client ID, API application-ID URI, signing key, exact callback URI and public origin are valid.
- Consumes: existing `AzureAd:Audience`, customer SPA client ID and customer consent callback configuration.
- Produces: customer `VITE_ENTRA_AUTHORITY` default/documentation `https://login.microsoftonline.com/organizations`; tenant authority override only from validated server resume.

- [ ] **Step 1: Add failing configuration/contract assertions.**

```csharp
[Fact] public void Missing_consent_first_registration_values_disable_only_anonymous_start()
{
    Assert.False(options.IsInvitationConsentConfigured);
    Assert.True(options.Validate(environment).IsAbsoluteUri);
}
```

Extend infra checks to require `Onboarding__CustomerClientId` and `Onboarding__ApiApplicationIdUri` as runtime configuration, reject these values from secret-bearing parameter blocks, and require the `organizations` default without changing platform-admin authority.
Add options assertions that trusted forwarded-header processing is disabled unless the configured proxy allowlist is valid, and that the invitation consent feature remains disabled when any required public consent setting is absent.

- [ ] **Step 2: Run the targeted tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~OnboardingOptionsTests`
Run: `python3 infra/tests/validate_contract.py`
Expected: FAIL because the new settings are absent from API options and deployment contract.

- [ ] **Step 3: Implement optional, fail-safe consent-first option validation.**

Do not make missing new values break unrelated startup/dev routes or legacy re-consent. Return sanitized configuration-unavailable for anonymous consent start until the new settings are complete. Require HTTPS and exact configured callback/public origins outside explicit localhost Development.

- [ ] **Step 4: Wire `.env.example`, Compose, Bicep, parameter examples, workflow variables and infra contract checks.**

Map `Onboarding__ApiApplicationIdUri` from the actual configured API audience/application URI, `Onboarding__CustomerClientId` from the customer SPA ID, and `Onboarding__TrustedProxyAddresses` only from explicitly configured ingress proxy addresses. Trust forwarded headers only from that allowlist; do not infer trust from `X-Forwarded-For`. Set the customer authority default to `https://login.microsoftonline.com/organizations`; do not change platform-admin authority. Do not place credentials, app secrets, tenant-specific runtime IDs, or permission consent in source or parameters.

- [ ] **Step 5: Run configuration and contract checks.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~OnboardingOptionsTests`
Run: `python3 infra/tests/validate_contract.py`
Run: `docker compose config --quiet`
Expected: PASS; both local and hosted configuration pass validation and platform-admin values remain unchanged.

- [ ] **Step 6: Commit.**

```bash
git add src/Api/Features/Workspaces/OnboardingOptions.cs .env.example docker-compose.yml infra/application.bicep infra/modules/container-apps.bicep infra/parameters/application-dev.example.json infra/parameters/application-prod.example.json .github/workflows/validate-and-deploy.yml infra/tests/validate_contract.py tests/Api.UnitTests/Workspaces/OnboardingOptionsTests.cs
git commit -m "feat: configure consent-first customer registration"
```

### Task 12: Configure existing Entra registrations with a dry-run-first script

**Files:**
- Create: `infra/scripts/configure-entra-onboarding.py`
- Create: `infra/tests/test_configure_entra_onboarding.py`
- Read-only source: `infra/entra/delegated-permissions.json`, `src/Api/Infrastructure/Graph/GraphScopeCatalog.cs:3-56`

**Interfaces:**
- Produces: CLI requiring expected home tenant ID, API app ID, customer SPA app ID, API application-ID URI, exact sign-in/consent redirect URIs; defaults to a sanitized dry-run diff and writes only with `--apply`.
- Consumes: `az account show`, Microsoft Graph `az rest`, checked permission manifest, and enabled live Graph `oauth2PermissionScopes`; no live tenant writes during tests.

- [ ] **Step 1: Write failing fake-Azure-CLI tests.**

```python
def test_default_is_sanitized_dry_run_and_apply_is_explicit(self):
    self.assertEqual(result.returncode, 0)
    self.assertIn("dry-run", result.stdout)
    self.assertNotIn("clientSecret", result.stdout)
    self.assertNotIn("admin consent granted", result.stdout)
```

Also test tenant mismatch, missing/disabled/ambiguous scopes, unexpected Graph application permissions, refusal of SPA Graph or `platform.admin`, preservation of non-Graph blocks/redirects, idempotent rerun, `--apply`, and a re-read failure after a partial write.

- [ ] **Step 2: Run the Python tests.**

Run: `python3 infra/tests/test_configure_entra_onboarding.py`
Expected: FAIL because the configuration script is missing.

- [ ] **Step 3: Implement the registration convergence script.**

Verify the current CLI tenant and both existing app objects before proposing changes. Resolve enabled Graph delegated scope IDs and the API's enabled `access_as_user` scope from live service-principal data. Set both registrations to `AzureADMultipleOrgs`, link only the customer SPA in API `knownClientApplications`, replace the API Graph block with manifest `Scope` permissions, set customer SPA API access to `access_as_user` only, merge the exact SPA/Web callbacks, preserve unrelated resource blocks, and re-read/verify after writes. Never grant consent, create secrets, alter customer tenants, assign roles, or modify the separate platform-admin SPA.

- [ ] **Step 4: Run fake CLI/Graph tests.**

Run: `python3 infra/tests/test_configure_entra_onboarding.py`
Expected: PASS without Azure credentials/network; dry-run is default and repeated apply converges.

- [ ] **Step 5: Commit.**

```bash
git add infra/scripts/configure-entra-onboarding.py infra/tests/test_configure_entra_onboarding.py
git commit -m "feat(infra): configure consent-first Entra apps"
```

### Task 13: Render the invitation landing page before sign-in

**Files:**
- Create: `src/Web/src/features/invitations/InvitationLandingPage.tsx`, `src/Web/src/features/invitations/pendingFlow.ts`
- Modify: `src/Web/src/features/invitations/invitationApi.ts:1-13`, `src/Web/src/features/invitations/InvitationRedemptionPage.tsx:1-18`, `src/Web/src/app/App.tsx:22-70`, `src/Web/src/auth/AuthProvider.tsx:43-59`, `src/Web/src/main.tsx:6-21`, `src/Web/src/messages/en.ts`
- Test: create `tests/Web.UnitTests/features/invitations/InvitationLandingPage.test.tsx` and `pendingFlow.test.ts`; retain `InvitationRedemptionPage.test.tsx`

**Interfaces:**
- Produces: `PendingInvitationFlow` with `kind: 'invitation' | 'workspace'`, `nonce?`, `challenge`, `tenantId?`, `expiresAt`, and `step`; read/write/clear helpers use `sessionStorage` only.
- Produces: `fetchInvitationPreview(nonce)`, `startInvitationConsent(nonce)`, and `resumeInvitationConsent(nonce, state, tenant?, errorCode?)` using same-origin JSON fetch without a bearer token.
- Produces: `InvitationLandingPage({ nonce }: { nonce: string })`; no workspace/session/capability API calls before consent or sign-in.

- [ ] **Step 1: Add failing landing/pending-flow tests.**

```tsx
it('renders anonymous preview and never asks MSAL for a token', async () => {
  expect(await screen.findByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' })).toBeTruthy();
  expect(getApiToken).not.toHaveBeenCalled();
  expect(fetch).not.toHaveBeenCalledWith('/api/session', expect.anything());
});
it('stores pending invitation state before leaving for consent', async () => {
  expect(sessionStorage.getItem(PENDING_FLOW_KEY)).toContain('"step":"consent_callback"');
  expect(locationAssign).toHaveBeenCalledWith(expect.stringContaining('/v2.0/adminconsent'));
});
```

Assert the exact copy:

```tsx
expect(screen.getByRole('heading', { name: 'Connect Demo workspace to Microsoft 365' })).toBeTruthy();
expect(screen.getByText("An administrator approves Atea's delegated permissions in Microsoft. Microsoft adds the enterprise applications to your tenant. Then sign in with the account invited by Atea.")).toBeTruthy();
expect(screen.getByText('Use an active Cloud Application Administrator, Application Administrator, Privileged Role Administrator or Global Administrator account. Your tenant may require MFA or PIM activation.')).toBeTruthy();
expect(screen.getByText('Consent does not assign Microsoft 365 roles. Actions remain limited by your signed-in account, workspace access and tenant policy.')).toBeTruthy();
expect(screen.getByRole('button', { name: 'Connect your Microsoft 365 tenant' })).toBeTruthy();
```

The expanded permission detail must show this exact catalog set: `User.Read`, `User.Read.All`, `Group.Read.All`, `Directory.Read.All`, `User.Create`, `User.ReadWrite.All`, `User.EnableDisableAccount.All`, `User-PasswordProfile.ReadWrite.All`, `User.RevokeSessions.All`, `GroupMember.ReadWrite.All`, `LicenseAssignment.ReadWrite.All`, `RoleManagement.Read.Directory`, `RoleManagement.ReadWrite.Directory`, `DeviceManagementManagedDevices.Read.All`, `DeviceManagementManagedDevices.ReadWrite.All`, `DeviceManagementManagedDevices.PrivilegedOperations.All`, `BitlockerKey.ReadBasic.All`, `BitlockerKey.Read.All`, `DeviceLocalCredential.ReadBasic.All`, `DeviceLocalCredential.Read.All`, `UserAuthenticationMethod.Read.All`, `UserAuthenticationMethod.ReadWrite.All`, and `MailboxSettings.Read`. A member preview presents normal sign-in/redemption rather than anonymous consent. Missing/expired preview returns **“Your invitation is no longer available. Ask Atea for a new invitation.”**

- [ ] **Step 2: Run the focused Vitest tests.**

Run: `npm run test:behavior --prefix src/Web -- --run InvitationLandingPage`
Run: `npm run test:behavior --prefix src/Web -- --run pendingFlow`
Expected: FAIL because invitation preview/start and tab transaction helpers do not exist.

- [ ] **Step 3: Implement the tab-scoped pending transaction helper.**

Persist nonce, expected challenge, expiry, flow kind and pending step before redirect. Compare callbacks to the exact stored challenge, enforce expiry, clear on completion/terminal failure, never move transaction data to local storage, URL parameters, telemetry, or another origin.

- [ ] **Step 4: Implement the anonymous landing route and copy.**

Render the single neutral setup panel with expandable exact scope details, status/error announcements, focus movement to asynchronous result headings, narrow-screen reflow, existing Atea logo/Inter/themes, and no third-party resources. Use the primary action text **“Connect your Microsoft 365 tenant”**. Do not reveal invitee identity, internal role/module assignments, tenant ID, or health diagnostics anonymously.

- [ ] **Step 5: Run invitation UI tests and build.**

Run: `npm run test:behavior --prefix src/Web -- --run InvitationLandingPage`
Run: `npm run test:behavior --prefix src/Web -- --run pendingFlow`
Run: `npm run build --prefix src/Web`
Expected: PASS; anonymous preview/start use no API token and tab state is written before navigation.

- [ ] **Step 6: Commit.**

```bash
git add src/Web/src/features/invitations/InvitationLandingPage.tsx src/Web/src/features/invitations/pendingFlow.ts src/Web/src/features/invitations/invitationApi.ts src/Web/src/features/invitations/InvitationRedemptionPage.tsx src/Web/src/app/App.tsx src/Web/src/auth/AuthProvider.tsx src/Web/src/main.tsx src/Web/src/messages/en.ts tests/Web.UnitTests/features/invitations/InvitationLandingPage.test.tsx tests/Web.UnitTests/features/invitations/pendingFlow.test.ts
git commit -m "feat(web): add anonymous consent-first invitation page"
```

### Task 14: Complete the tenant-pinned callback and automatic onboarding flow

**Files:**
- Modify: `src/Web/src/auth/AuthProvider.tsx:8-65`, `auth/msalConfig.ts:1-22`, `app/App.tsx:52-70`, `app/routes.tsx:33-40`
- Modify: `src/Web/src/features/invitations/InvitationLandingPage.tsx`, `features/invitations/invitationApi.ts`, `features/invitations/pendingFlow.ts`
- Modify: `src/Web/src/features/workspace-settings/ConsentCallbackPage.tsx:1-50`, `OnboardingPage.tsx:8-75`
- Test: `tests/Web.UnitTests/auth/AuthProvider.test.tsx:50-125`, `auth/InvitationAuthRedirect.test.tsx:1-24`, `features/invitations/InvitationRedemptionPage.test.tsx`; update `tests/Web.E2E/consent-callback.spec.tsx:1-23`

**Interfaces:**
- Produces: `signInForTenant(tenantId: string, returnPath: string): Promise<void>` and tenant-aware `getApiToken(expectedTenantId?: string): Promise<string>`; account selection must match expected `AccountInfo.tenantId`, never implicitly choose `accounts[0]`.
- Consumes: server-validated `resume` result `tenantId`; authority is exactly `https://login.microsoftonline.com/{tenantId}`.
- Produces: invitation flow order `resume -> tenant-pinned sign-in -> one redemption -> one authenticated completion -> health result/overview`; legacy workspace flow remains authenticated `complete -> baseline check`.

- [ ] **Step 1: Add failing auth and callback-flow tests.**

```tsx
it('uses the server tenant authority and refuses an account cached in another tenant', async () => {
  expect(instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({
    authority: 'https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111',
    prompt: 'select_account',
  }));
  await expect(getApiToken('11111111-1111-1111-1111-111111111111')).rejects.toThrow();
});
it('redeems and completes once across StrictMode rerenders', async () => {
  expect(redeem).toHaveBeenCalledTimes(1);
  expect(complete).toHaveBeenCalledTimes(1);
});
```

Also assert a missing/mismatched tab challenge, wrong callback tenant, denied consent, or missing transaction does not sign in or redeem; callback query parameters are removed with `history.replaceState`; recovery submits the still-valid invitation challenge; a second identity cannot use the recovery path.

- [ ] **Step 2: Run focused web tests.**

Run: `npm run test:behavior --prefix src/Web -- --run AuthProvider`
Run: `npm run test:behavior --prefix src/Web -- --run InvitationAuthRedirect`
Run: `npm test --prefix tests/Web.E2E -- consent-callback`
Expected: FAIL because MSAL account/authority pinning and callback dispatch are not implemented.

- [ ] **Step 3: Implement tenant-pinned MSAL actions and route gating.**

Keep `MsalProvider` initialized for public paths, but render invitation and `/onboarding/consent/callback` before `AuthenticatedContent`'s sign-in block. Select only the account matching the server tenant for silent/redirect token acquisition. Preserve the invitation redirect-start-page behavior and leave `/admin`'s separate provider/authority unchanged.

- [ ] **Step 4: Dispatch callbacks by persisted flow, not callback claims.**

For invitation flow, require the pending state to exactly match the callback state and call anonymous resume; denial shows safe guidance without sign-in, success starts MSAL at the returned tenant authority and returns to `/invitations/{nonce}`. Remove query parameters after consuming callback fields. If no tab transaction exists, show **“Open your original invitation and try again.”** Legacy workspace flow keeps authenticated completion then its separate automatic baseline check; an old unsolicited legacy callback offers workspace-settings re-consent.

- [ ] **Step 5: Redeem, complete and display truthful verification.**

After authenticated account selection, automatically redeem once; persist the pending step across redirect/lost response and deduplicate in-flight work under StrictMode/double clicks. Do not render the workspace shell until redemption succeeds. Then complete the original state once and render `health`/coverage: connected continues to `/overview`; partial shows **“Connected to Microsoft, but some delegated permissions are unavailable.”** with exact missing scopes and re-consent; unknown checks show retry guidance and do not claim full coverage. Keep membership after transient failure and let retry call comprehensive health check, never replay consumed state.

- [ ] **Step 6: Update the authenticated onboarding re-consent action.**

Before navigating to the existing workspace consent URL, store a `workspace` pending transaction with its challenge and the validated tenant from that trusted URL. Do not store an invitation nonce in the workspace flow. Preserve the legacy API-client/Graph-`.default` URL and existing onboarding/member behavior.

- [ ] **Step 7: Run focused frontend tests and production build.**

Run: `npm run test:behavior --prefix src/Web -- --run AuthProvider`
Run: `npm run test:behavior --prefix src/Web -- --run InvitationAuthRedirect`
Run: `npm test --prefix tests/Web.E2E -- consent-callback`
Run: `npm run build --prefix src/Web`
Expected: PASS; invitation path makes one redeem and one completion; legacy consent still completes then checks once.

- [ ] **Step 8: Commit.**

```bash
git add src/Web/src/auth/AuthProvider.tsx src/Web/src/auth/msalConfig.ts src/Web/src/app/App.tsx src/Web/src/app/routes.tsx src/Web/src/features/invitations/InvitationLandingPage.tsx src/Web/src/features/invitations/invitationApi.ts src/Web/src/features/invitations/pendingFlow.ts src/Web/src/features/workspace-settings/ConsentCallbackPage.tsx src/Web/src/features/workspace-settings/OnboardingPage.tsx tests/Web.UnitTests/auth/AuthProvider.test.tsx tests/Web.UnitTests/auth/InvitationAuthRedirect.test.tsx tests/Web.UnitTests/features/invitations/InvitationRedemptionPage.test.tsx tests/Web.E2E/consent-callback.spec.tsx
git commit -m "feat(web): finish consent-first invitation onboarding"
```

### Task 15: Update rollout documentation and run full verification

**Files:**
- Modify: `README.md:24-69,71-99`
- Modify: `docs/testing/test-tenant.md:20-75`
- Modify: `docs/operations/azure-deployment.md` (Prerequisites, callback table, GitHub variables, Configuration mapping, rollout/rollback)
- Modify: `docs/security/entra-app-registration.md` (registration, permissions, consent handoff)
- Modify: `docs/security/threat-model.md:14-37,64-88`
- Modify: `docs/security/data-isolation.md:1-11`

**Interfaces:**
- Documentation describes the actual API, manifest, script flags, environment mappings, callback URIs, test commands and rollout prerequisite; it does not claim live-tenant validation.
- Verification consumes all test suites and build/contract commands established above. Container-backed integration tests use `Testcontainers.PostgreSql`; `DatabaseMigrationRunnerTests` starts PostgreSQL 16 Alpine without a Docker-unavailable skip, while some other container tests explicitly skip when Docker is absent.

- [ ] **Step 1: Update documentation from the implemented interfaces.**

Document domain-or-GUID operator provisioning, explicit choice of tenant (no email-domain inference), the anonymous one-link consent-first sequence, the delegated-only role/PIM boundary, customer SPA `knownClientApplications` bundling and API-resource `/.default` versus retained legacy Graph `/.default`. Add script dry-run/`--apply` instructions, new runtime mappings, exact primary callback registration, PostgreSQL/Docker requirements, failure/recovery cases, safe evidence rules, migration-before-app rollout, opt-in fresh-tenant acceptance gate and rollback behavior. Clarify that consent-first validation must run on the configured primary origin and cannot copy pending `sessionStorage` to a candidate host.

- [ ] **Step 2: Run API unit tests.**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj`
Expected: PASS.

- [ ] **Step 3: Run API integration tests with Docker available.**

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj`
Expected: PASS; Testcontainers PostgreSQL migration, atomicity and race tests execute. If Docker is unavailable, record the environmental failure/skip accurately; do not replace these tests with an in-memory database.

- [ ] **Step 4: Run all web tests and the production build.**

Run: `npm test --prefix tests/Web.UnitTests`
Run: `npm run test:behavior --prefix src/Web -- --run`
Run: `npm test --prefix tests/Web.E2E`
Run: `npm run build --prefix src/Web`
Expected: PASS; the `tests/Web.E2E` command remains Vitest/jsdom and is not represented as a real browser or Entra acceptance test.

- [ ] **Step 5: Run infra/Python and Compose contract checks.**

Run: `python3 infra/tests/test_configure_entra_onboarding.py`
Run: `python3 infra/tests/validate_contract.py`
Run: `docker compose config --quiet`
Expected: PASS without Azure credentials or live registration changes.

- [ ] **Step 6: Review the full diff and confirm only documentation/integration fixes accompany this final task.**

Run: `git diff --check && git status --short`
Expected: no whitespace errors; only intended spec-related files are modified.

- [ ] **Step 7: Commit.**

```bash
git add README.md docs/testing/test-tenant.md docs/operations/azure-deployment.md docs/security/entra-app-registration.md docs/security/threat-model.md docs/security/data-isolation.md
git commit -m "docs: document consent-first onboarding and rollout"
```

## Plan Self-Review

- **Spec coverage:** tenant-domain normalization/discovery and canonical provisioning (Tasks 1–3); additive persistence, legacy-compatible versioned state, invitation eligibility/preview/start/resume, recovery, atomic consume and reissue/revoke behavior (Tasks 4–7); bounded delegated scope probing, truthful health/status transitions, and baseline retry semantics (Tasks 8–9); permission manifest, registration script, environment and callbacks (Tasks 10–12); public SPA, MSAL, invitation redemption/completion, retained legacy flow and exact copy (Tasks 13–14); security docs, rollout gate, Docker requirements and full verification (Task 15).
- **Step scan:** Every implementation task writes a failing test, runs that selector, implements one boundary, reruns it, and commits explicit files; tests name expected values and failure outcomes. No task requires live Azure registration writes.
- **Type consistency:** Provisioning inputs resolve to `Guid` before existing provisioning service calls; invitation-state hashes bind the new nullable invitation key; `PermissionCoverage` and `ConnectionHealthDto` are shared by completion and explicit retry; `PendingInvitationFlow` carries the server-returned tenant through MSAL without trusting callback query values.
- **Review Focus:** All five listed risks have a corresponding resolver, API, PostgreSQL, verifier, or browser/auth test in the task that owns the behavior.
- **Proportion:** Fifteen bounded tasks cover a cross-cutting 306-line specification without reproducing implementation bodies; algorithm detail is limited to trust checks, state binding, rate limits, and verifier classification.
