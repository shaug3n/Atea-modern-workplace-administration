# Azure Deployment Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing application deployable, operable, and testable in the owner's Azure test subscription without enabling the development-only Atea admin login, then make the same deployment procedure repeatable in an Atea-owned subscription.

**Architecture:** Keep one combined SPA/API container and delegated Microsoft Graph access. Split Azure provisioning into a foundation deployment and an application deployment so the generated HTTPS host, Entra redirects, registry image, Key Vault secrets, and private-database migrations can be established in a valid order. Add Entra federation for the platform console, a finite migration job, shared cryptographic keys, and revision-gated releases.

**Tech Stack:** React/Vite/MSAL, ASP.NET Core and EF Core, PostgreSQL Flexible Server, Azure Container Apps/Jobs, ACR, Key Vault, Blob Storage, Bicep, GitHub Actions OIDC.

**Spec:** [Atea Unified Digital Workplace Platform](../specs/2026-09-17-atea-unified-workplace-design.md) and [Atea platform-admin console design](../specs/2026-09-21-atea-platform-admin-design.md). The approved admin spec explicitly defers federation; this plan implements that deferred Azure prerequisite.

## Global Constraints

- First deployment is disposable and test-tenant-only; use the owner's test Azure subscription, a separate hosted Entra SPA/API pair, test users, and no customer production data.
- The later Atea deployment creates new Azure resources, Entra registrations, secrets, hostnames, and consent; it does not repoint a test-tenant application by changing one tenant ID.
- Local Atea username/password authentication remains Development-only. Azure test runs as `Staging`, and an Atea deployment runs as `Production`; neither may enable the local provider.
- Customer Microsoft 365 calls remain delegated on behalf of the signed-in customer or B2B guest. Platform-admin access never grants Graph rights or bypasses the verified customer tenant/workspace boundary.
- Platform-admin authorization requires a tenant-pinned Entra identity, the `platform.admin` API scope, an explicit operator allowlist, and a workspace grant. Newly provisioned workspaces grant their creator platform-metadata access transactionally; existing workspaces do not become implicitly visible.
- SPA redirects and API consent redirects are distinct exact HTTPS URIs. No wildcard redirects or Atea-owned hostname in the personal test deployment.
- Runtime and migration database credentials are separate. Do not pass plaintext secrets through SPA build args, source control, Bicep parameter files, logs, or invitations.
- An Azure free-account credit is a cost constraint, not a guarantee that the deployed services fit within it. Set budget alerts and review forecast before provisioning; do not remove a spending limit without an explicit user decision.
- Code, Bicep, and CI changes can be prepared without Azure credentials; creating billable resources and seeding real secrets are separate live-validation steps using the owner's chosen test subscription.
- Do not enable external-customer access or privileged tenant mutations until Daybreak findings have been reviewed and blocking issues remediated. A private, test-user-only smoke deployment may happen earlier if its risk is accepted by the owner.
- Target .NET 10 LTS for the sustained hosted pilot; .NET 9 remains supported only until 2026-11-10 ([support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)).
- Each task uses a failing test/contract check before implementation, then focused verification, then a commit. Preserve unrelated untracked files.

## Review Focus

1. A token with the right `oid` but the wrong `tid`, or a customer token without `platform.admin`, must not access `/api/platform/*` (Task 2).
2. A newly created workspace must become manageable by its provisioner immediately, while another platform operator still cannot see it (Task 2).
3. Wrong SPA/API redirect pairing or absent hosted secrets must fail before the app is marked ready, without printing the secret (Task 4).
4. A second replica must be able to validate an audit/device continuation token produced by the first replica; a restart must not erase the key ring (Task 4).
5. A failed migration, unhealthy revision, or failed authenticated smoke test must leave the prior revision at 100% traffic; first deployment must remain unadvertised (Task 7).

---

### Task 1: Pin a supported hosted runtime and establish the deployment baseline

**Files:** Modify `src/Api/Atea.UnifiedWorkplace.Api.csproj`, `tests/Api.UnitTests/Api.UnitTests.csproj`, `tests/Api.IntegrationTests/Api.IntegrationTests.csproj`, `Dockerfile`, `.github/workflows/validate-and-deploy.yml`; create `global.json`. Test with existing .NET, frontend, and Compose suites.

**Interfaces:** Produces one versioned build contract (target framework, SDK, runtime image, compatible EF/Npgsql packages) for all following tasks. No application API changes.

- [ ] **Step 1: Record the baseline.** Run `dotnet test Atea.UnifiedWorkplace.sln -c Release`, `npm ci --prefix src/Web`, `npm run test:behavior --prefix src/Web -- --run`, `npm run build --prefix src/Web`, `docker compose config --quiet`, and `python3 infra/tests/validate_contract.py`; record failures without changing unrelated files.
- [ ] **Step 2: Write a failing runtime-contract test.** Add an assertion to `infra/tests/validate_contract.py` that the API/test projects, Docker SDK/runtime stages, and CI use the same supported .NET major version. Run the validator; expect a .NET 9 failure.
- [ ] **Step 3: Upgrade and pin.** Move the API and tests to `net10.0`; use the matching .NET 10 SDK/runtime images and compatible EF Core/Npgsql/Identity.Web packages after checking their current supported versions. Pin the then-current supported .NET 10 SDK feature band in `global.json`, not an unbounded future major. The owner's machine currently has only .NET 9, so install .NET 10 or run the .NET tests in the SDK container before requiring a native local test pass.
- [ ] **Step 4: Verify.** Rerun the validator, .NET tests, frontend build, and Docker build. Any migration-model diff must be reviewed; a runtime upgrade must not silently add schema changes.
- [ ] **Step 5: Commit** only runtime-contract and version changes.

### Task 2: Authorize hosted platform operators without expanding customer privileges

**Files:** Modify `src/Api/Authorization/PlatformAuthorization.cs`, `src/Api/Features/Workspaces/WorkspaceEndpoints.cs`, `src/Api/Infrastructure/Persistence/WorkplaceDbContext.cs`, the workspace provisioning repository/service and one additive migration; create `src/Api/Infrastructure/Persistence/Entities/PlatformWorkspaceGrant.cs`. Test in `tests/Api.UnitTests/Authorization/PlatformAuthorizationTests.cs`, `tests/Api.IntegrationTests/Workspaces/WorkspaceEndpointsTests.cs`, and `tests/Api.IntegrationTests/Security/CrossTenantAccessTests.cs`.

**Interfaces:** Consume validated Entra `tid`, `oid`, and `scp`; produce `IPlatformAuthorization.IsAuthorized(ClaimsPrincipal)` and asynchronous `GetWorkspaceScopeAsync(ClaimsPrincipal, CancellationToken)` / `CanManageWorkspaceAsync(ClaimsPrincipal, Guid, CancellationToken)`. The grant table key is `(OperatorTenantId, OperatorObjectId, WorkspaceId)`; it conveys platform metadata scope only.

- [ ] **Step 1: Write failing authorization tests.** Pin success for the configured test-tenant operator with `platform.admin`; reject wrong `tid`, missing scope, absent allowlist, and customer workspace tokens. Pin local Development-cookie behavior separately.
- [ ] **Step 2: Write a failing provisioning test.** Onboarding a workspace and its first invitation must atomically create a grant for the acting platform operator. A second operator must see no workspace until explicitly granted; a rollback must leave no workspace, invitation, or grant.
- [ ] **Step 3: Implement tenant-pinned operator configuration and grant persistence.** Retain explicit recovery scopes for preexisting workspaces and remove any need for an Azure `AllowAllWorkspaces` escape hatch. Read scope from the database rather than requiring a process restart after each new workspace.
- [ ] **Step 4: Verify.** Run focused authorization, provisioning, migration, and cross-tenant tests. Test an existing database migration to confirm no customer membership or invitation changes.
- [ ] **Step 5: Commit** the server authorization boundary and additive migration.

### Task 3: Federate the Atea admin UI in hosted environments

**Files:** Modify `src/Web/src/features/admin/AdminApp.tsx`, `adminAuthApi.ts`, `adminApi.ts`, `src/Web/src/auth/msalConfig.ts`, `src/Web/src/main.tsx`, `Dockerfile`, `src/Api/Features/AdminAuth/AdminAuthEndpoints.cs`; create a focused `src/Web/src/features/admin/HostedAdminAuth.tsx`. Test in `tests/Web.UnitTests/features/admin/AdminApp.test.tsx`, `AdminAuthApi.test.tsx`, `tests/Web.E2E/admin-onboarding.spec.tsx`, and `tests/Api.IntegrationTests/Features/AdminAuth/AdminAuthEndpointTests.cs`.

**Interfaces:** Hosted SPA requests `api://<hosted-api-client-id>/platform.admin` from a tenant-specific authority and sends the bearer token only to `/api/platform/*`. `GET /api/platform/session` returns safe platform-admin identity metadata after the platform policy/allowlist checks. Hosted admin redirect is the exact `https://<host>/admin/auth/callback`; customer sign-in keeps its own `access_as_user` scope and `/auth/callback`. The same SPA registration may expose both redirects, but neither route may consume the other's result. Development retains the existing cookie flow.

- [ ] **Step 1: Write failing UI tests.** In hosted mode, unauthenticated `/admin` starts Entra sign-in rather than rendering the local password form; a completed callback loads the admin shell; sign-out clears the hosted account; 401/403 have distinct actions. Development still renders the local form.
- [ ] **Step 2: Write failing API tests.** A hosted platform-session request with a valid bearer token returns safe admin identity metadata, while a local cookie is rejected outside Development and a platform bearer token cannot enter `/api/session` without customer membership.
- [ ] **Step 3: Implement the two auth paths.** Use a hosted MSAL instance with the platform scope and callback; add bearer injection to platform API requests only. Do not put the API client secret in `VITE_*` values or browser storage.
- [ ] **Step 4: Verify.** Run admin frontend unit/E2E tests and API integration tests. Confirm old local admin tests still pass and no hosted route accepts username/password.
- [ ] **Step 5: Commit** the hosted admin flow.

### Task 4: Validate hosted configuration and share cryptographic keys

**Files:** Modify `src/Api/Program.cs`, `infra/modules/key-vault.bicep`, `infra/modules/container-apps.bicep` (later split by Task 6), `src/Api/Atea.UnifiedWorkplace.Api.csproj`; create `src/Api/Infrastructure/Configuration/HostedConfigurationValidator.cs`; add unit/integration tests in `tests/Api.UnitTests/Infrastructure/` and `tests/Api.IntegrationTests/`. Include Blob Storage/Data Protection wiring in a focused infrastructure file rather than enlarging `Program.cs` further.

**Interfaces:** Hosted config requires `AzureAd:ClientId`, `AzureAd:Audience`, `AzureAd:ClientSecret`, `PlatformAuthorization:HomeTenantId`, operator allowlist, `Onboarding:PublicBaseUrl`, `Onboarding:ConsentRedirectUri`, `Onboarding:ConsentSigningKey`, `Users:ContinuationSigningKey`, `ConnectionStrings:WorkplaceDb`, and a managed-identity-backed Data Protection key store. `PublicBaseUrl` and callbacks must agree on the same HTTPS origin, but the customer, hosted-admin, and API-consent callback paths remain distinct.

- [ ] **Step 1: Write failing validation tests.** Missing or mismatched hosted values, Development-only admin settings in Staging, HTTP hosts, and identical consent/sign-in callbacks must fail startup with a field name but no secret value. Development fixture behavior stays unchanged.
- [ ] **Step 2: Write a failing cross-instance token test.** Create an audit and device continuation token with one Data Protection provider; recreate the provider as a second instance against the same key store; both tokens must validate. An isolated key store must fail validation.
- [ ] **Step 3: Implement configuration validation and Key Vault references.** Add named secrets for API client secret, runtime DB connection, consent-signing key, and continuation-signing key. Keep the PostgreSQL migration credential separate. Use Blob-backed Data Protection keys and Key Vault key encryption with managed identity; grant only the required storage/key permissions. Azure test uses `Staging`, never `Development`.
- [ ] **Step 4: Add readiness semantics.** Keep `/health` as a process liveness probe; add `/health/ready` that checks the migrated database and required configuration without depending on Graph being available. Test DB failure returns non-200, and Graph outage does not falsely mark the process dead.
- [ ] **Step 5: Verify.** Run focused tests, then run a two-replica local/integration simulation for cross-instance token validation. Check logs and responses for accidental secret disclosure.
- [ ] **Step 6: Commit** the configuration and key-ring work.

### Task 5: Make database migration a finite, private-networked release gate

**Files:** Modify `src/Api/Program.cs`; create `src/Api/Infrastructure/Persistence/DatabaseMigrationRunner.cs`; modify `infra/modules/container-apps.bicep` or its Task 6 successor to define a manual Container Apps Job. Test in `tests/Api.IntegrationTests/Persistence/DatabaseMigrationRunnerTests.cs` and `infra/tests/validate_contract.py`.

**Interfaces:** `DatabaseMigrationRunner.RunAsync(IServiceProvider, CancellationToken): Task<int>` applies EF migrations once using `ConnectionStrings:WorkplaceMigrationDb`, returns exit code 0 on success, and exits without starting the HTTP listener. The exact command is `dotnet Atea.UnifiedWorkplace.Api.dll --migrate`. Normal app requests use `ConnectionStrings:WorkplaceDb`. The job uses the same immutable image tag as the candidate app revision, migration-only DB credentials, no ingress, one execution at a time, and the Container Apps network that can reach private PostgreSQL.

- [ ] **Step 1: Write failing tests.** `--migrate` against a fresh PostgreSQL test database creates the schema and exits 0; an invalid connection exits nonzero; invoking it twice is idempotent; normal hosted startup never calls `MigrateAsync`.
- [ ] **Step 2: Implement the runner and command dispatch.** Keep current Development auto-migration only for local use. Never turn on Development in Azure as a migration shortcut.
- [ ] **Step 3: Define runtime versus migrator database access.** Provision a runtime DB role without schema-change privileges and a migration role with the minimum DDL rights required by EF; store separate connections in Key Vault. Pin in an integration test that the runtime credential cannot apply a migration.
- [ ] **Step 4: Define and validate the manual job.** Assert that the Bicep job's command, network, image tag, secrets, replica/parallelism, and bounded timeout match the contract. A plain web-server image without `--migrate` must fail validation.
- [ ] **Step 5: Verify** the runner using local PostgreSQL and a container execution that exits rather than serving forever; commit.

### Task 6: Break the Azure bootstrap cycle with staged Bicep

**Files:** Rework `infra/main.bicep` into the foundation entrypoint; create `infra/application.bicep` and focused environment/app/job modules under `infra/modules/`; modify `infra/parameters/dev.json`, `infra/parameters/prod.example.json`, and `infra/tests/validate_contract.py`; update `docs/operations/azure-deployment.md` when the interfaces settle.

**Interfaces:** Foundation outputs ACR login server, Key Vault URI, private PostgreSQL FQDN, managed-identity ID, Container Apps environment ID/default domain, storage key-ring URI, and monitoring IDs. Application consumes those outputs, immutable image tag, exact HTTPS public base URL, distinct SPA/admin/API consent redirect URIs, and no secret plaintext. `allowedIngressHostnames=[]` is valid for the first test release; optional custom-domain/certificate binding is a later DNS-dependent stage.

- [ ] **Step 1: Write failing contract checks.** Assert that foundation has no image/secret/certificate dependency, that application accepts an empty custom-domain list, and that public base URL and API consent redirect are separate parameters. Run the validator; expect failure against the current one-shot template.
- [ ] **Step 2: Split modules.** Foundation creates network, private PostgreSQL/DNS, ACR, identity, Key Vault, monitoring, Blob key storage, and the Container Apps environment. Application creates the migration job and combined app only after image and Key Vault secrets exist. Do not create a managed certificate before an app and its DNS validation exist.
- [ ] **Step 3: Make the host order deterministic.** Read the environment's default domain from foundation outputs; derive the app's generated FQDN; register exact Entra callbacks; build the SPA with those public values; then deploy the application. Fail if the built host and deployed `Onboarding:PublicBaseUrl` disagree.
- [ ] **Step 4: Compile and preview.** Run `python3 infra/tests/validate_contract.py`, `az bicep build --file infra/main.bicep`, and `az bicep build --file infra/application.bicep`. With Azure credentials later, run `az deployment group what-if` for each stage before `create`.
- [ ] **Step 5: Commit** staged infrastructure and updated parameter examples. Example files contain no real secrets or assumed Atea-owned test hostname.

### Task 7: Make CI/CD provisionable, gated, and rollback-safe

**Files:** Modify `.github/workflows/validate-and-deploy.yml`; optionally create a small `.github/workflows/provision-test.yml` for manual foundation provisioning; extend `infra/tests/validate_contract.py`; update `docs/operations/azure-deployment.md`.

**Interfaces:** PR and push CI follows `master`; deployment is manually dispatched to a protected `test` GitHub environment using OIDC. The deployment identity receives only the resource-group, ACR-push, Key Vault seeding, and job-start rights it needs. Candidate app and migration job use the same SHA-tagged image. The currently serving revision retains 100% traffic until migration and authenticated smoke checks pass.

- [ ] **Step 1: Write failing workflow-contract tests.** Detect a `main`-only trigger, reference to a nonexistent migration job, image build before hosted public variables are available, and `latestRevision: true`/100% routing before smoke.
- [ ] **Step 2: Implement the first-deploy flow.** Manual foundation `what-if`/create; read generated host; verify Entra registrations and Key Vault names exist; build and scan the immutable image with the four customer `VITE_ENTRA_*` values and hosted-admin public scope/authority/redirect; push to ACR; run the migration job before starting the web app. Start the first app with external ingress disabled or access-restricted, smoke-test it from an approved network path, then explicitly enable HTTPS ingress for the approved test users. A first revision has no previous revision to hold traffic, so do not pretend a 0%-traffic rule alone protects it.
- [ ] **Step 3: Implement subsequent-release flow.** Preserve the current revision weight, back up PostgreSQL or verify a recent restorable backup, run backward-compatible migration, create candidate revision at 0%, test its revision-specific URL with a real test-tenant token, then route traffic. On any failure, leave the old revision at 100%; record rollback instructions for an already-routed failure. Do not claim database rollback from traffic switching alone.
- [ ] **Step 4: Fix workflow variable scope and permissions.** Public `VITE_*` values must be available to the job that actually builds the release image; secrets remain GitHub environment secrets or Key Vault values. Configure GitHub OIDC federated credentials for the exact repository/environment subject; do not create a long-lived Azure client secret.
- [ ] **Step 5: Verify** YAML parsing, local contract tests, dry-run `what-if`, a deliberately failing migration, a deliberately failing smoke test, and a successful test-resource release. Record the old/new traffic weights in each case; commit.

### Task 8: Prove the hosted test journey and document the Atea handoff

**Files:** Extend `tests/RealTenant/README.md`, `tests/RealTenant/Scenarios/OnboardingScenario.cs`, `tests/Web.E2E/admin-onboarding.spec.tsx`, `docs/operations/azure-deployment.md`, and `README.md`. Add only focused smoke scripts under `infra/scripts/` if they provide checks the workflow cannot express cleanly.

**Interfaces:** A deployment evidence record contains subscription/tenant ID, region, image SHA, exact public host, migration execution ID, API readiness result, revision name/traffic weights, consent outcome, and redacted test results. It contains no client secret, token, TAP, BitLocker key, or invitation nonce.

- [ ] **Step 1: Add hosted journey tests.** A platform operator signs in with Entra, creates a workspace/invitation, sees it immediately, and a nominated customer admin redeems it and reaches setup. The hosted local-password route stays unavailable.
- [ ] **Step 2: Test delegated permissions in the actual test tenant.** Global Reader reaches permitted read views but cannot mutate; User Administrator/PIM test accounts perform only authorized operations; missing consent and expired/inactive PIM show the correct guidance. Confirm `/api/session`, `/api/capabilities`, connection health, and a database-backed endpoint with a real hosted token.
- [ ] **Step 3: Write the operator runbook.** Include subscription/directory verification, cost estimate and budget alerts, exact app-registration steps, OIDC setup, foundation and application commands, Key Vault secret names and safe seeding, migration, smoke/rollback, audit/log checks, and safe teardown of the disposable test resource group. Do not tell the owner to run the old one-shot `infra/main.bicep` plus current workflow.
- [ ] **Step 4: Add the Atea migration checklist.** New Atea subscription/resource group, Atea-owned hostname and certificate, new Entra registrations and consent, new Key Vault secrets/OIDC trust, database backup/restore decision, test-tenant data disposal, and customer re-onboarding if identity or service-principal IDs change.
- [ ] **Step 5: Apply the Daybreak gate.** Classify each finding, fix release-blocking issues, rerun affected tests and the hosted journey, then record the owner's explicit decision before external access or privileged tenant operations. Unknown Daybreak results are not assumed clean.
- [ ] **Step 6: Verify** full .NET/frontend/Compose/Bicep checks and a real test-tenant browser run; link the evidence in the runbook; commit.

## Deployment acceptance gate

The test release is ready only when Tasks 1–8 pass, the GitHub run can provision a fresh resource group without manual template surgery, no local admin login exists in Azure, the first hosted workspace is onboarded using Entra, Graph operations respect the test users' roles/PIM state, migration and revision rollback drills behave as documented, and the owner has reviewed cost and Daybreak results. This is not a claim that the current repository already meets those conditions.
