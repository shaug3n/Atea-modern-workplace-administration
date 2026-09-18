# Task 4 report: onboarding, invitation instructions and connection health

## Status

Implemented and committed as `feat: add tenant onboarding and connection health`.

## Implementation

- Added the explicit seven-state onboarding state machine:
  `awaiting_invitation`, `consent_required`, `connected`,
  `permission_incomplete`, `temporarily_unavailable`, `consent_revoked`,
  and `connection_failed`.
- Added `OnboardingService` with transition validation and safe connection-state persistence.
- Added `InvitationService` using 32 random bytes and lowercase SHA-256 nonce hashes. The plaintext nonce is used only to construct the one-time URL returned from creation and is not persisted or logged.
- Added repository-bound invitation persistence and redemption. Redemption validates expiry, tenant, invited email and unused state, then atomically marks the invitation redeemed, creates membership and moves the workspace to `consent_required` in one database transaction.
- Added platform-admin-only invitation creation and authenticated invitation redemption endpoints.
- Added `IConnectionHealthReader`, a delegated-probe boundary, safe result mapping, and a replaceable unconfigured probe for the Task 5 adapter.
- Added current-workspace health, health-check, and consent-start endpoints. Consent start returns only an authorization URL/challenge descriptor and documented scopes; no Graph token is returned.
- Preserved `/health` and the existing Task 2 authentication/workspace middleware behavior, with the authenticated invitation redemption path explicitly exempted from membership resolution so a new invite can be redeemed.
- Added typed English connection messages, overview/onboarding pages, and an explicit status card for every state.
- Added the Graph permission matrix and test-tenant handoff/runbook documentation.

## Security checks

- Endpoints do not query `WorkplaceDbContext` directly; persistence is behind repository/service boundaries.
- The database entity stores `NonceHash`, never the plaintext invitation nonce.
- The invitation response is limited to the authorized platform-admin route.
- Redemption does not expose the nonce after creation, and invalid/expired/redeemed invitations return a safe problem code.
- Graph access is represented by a delegated probe boundary; no Graph token or raw authorization header is returned or logged.
- A source scan found no `access_token`, `refresh_token`, `client_secret`, bearer-header logging, or logger calls in the new onboarding/Graph/UI paths.

## Tests and checks

Using `DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home`, `NUGET_PACKAGES=/private/tmp/atea-nuget` and `--disable-build-servers`:

- API unit tests: **31 passed, 0 failed**.
- Non-container API integration subset (health, authorization and onboarding endpoint protection): **14 passed, 1 skipped**. The skip is the existing opt-in real-Entra test.
- API integration project build: **passed, 0 warnings, 0 errors**.
- Frontend behavior tests: **12 passed, 0 failed**.
- Frontend production build: **passed**. Vite emitted its existing bundle-size advisory for the main JavaScript chunk.
- Full API integration suite was attempted. **19 passed, 2 failed because Testcontainers could not connect to the unavailable Docker daemon**; no Docker daemon was started or installed. The two failures are the existing PostgreSQL repository tests that require Testcontainers.
- `git diff --check`: passed.

The Graph scope matrix was checked against Microsoft’s official [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference) on 2026-09-18.

## Concerns / follow-up

- `UnconfiguredDelegatedConnectionProbe` intentionally reports `temporarily_unavailable` until Task 5 wires the real delegated Graph adapter.
- PostgreSQL-backed invitation atomicity and repository isolation still need the Docker/Testcontainers environment for execution; the repository implementation uses an explicit EF transaction and should be exercised when Docker is available.
- The v1 consent scope set includes broad read scopes documented in the matrix; Task 5 should narrow them where focused Graph operations allow it.
- The current UI is a minimal state-aware shell; API data fetching and full consent redirect completion remain subsequent integration work.
