# Dedicated Entra test-tenant runbook

This runbook covers the existing onboarding checks and the Task 14 manual real-tenant scenarios. Use only a dedicated, non-production tenant and a separate test app registration. The checked-in scenario manifest is under `tests/RealTenant/`; the repository has no real-tenant test project and these scenarios are intentionally not part of CI.

## Customer-admin handoff

1. An authorized Atea platform administrator provisions the workspace and creates an invitation instruction for the customer administrator.
2. The instruction is copied once from the API response and handed to the customer administrator through the agreed customer channel. The API does not send email, log the URL or store the nonce.
3. The customer administrator signs in to Entra ID in the customer tenant and redeems the instruction. Redemption requires the invitation email to match, the token tenant to match the workspace tenant, and the nonce to be unused and unexpired.
4. The redemption transaction creates the customer membership, marks the invitation redeemed and moves the workspace to `consent_required`.

## Atea B2B guest completion path

1. Invite the Atea operator as a B2B guest in the customer tenant and complete redemption in the customer tenant browser session.
2. Assign only the customer-approved directory role needed for the test case (normally Directory Readers; use Privileged Role Administrator only for role/PIM scenarios).
3. Start delegated consent from workspace settings, complete customer admin consent, then run a connection check.
4. Validate the expected state: `connected`, `permission_incomplete`, `consent_revoked` or `temporarily_unavailable`. Revoke consent and repeat the check to verify the revoked path.

Record only tenant/object identifiers, granted scope names, connection state and correlation IDs in test evidence. Never record access tokens, invitation nonces or raw authorization headers.

## Local limitation

The integration suite includes Testcontainers PostgreSQL coverage, but Docker/Testcontainers execution requires a running Docker daemon. If Docker is unavailable, run unit tests and non-container frontend checks and record the skipped container-backed tests; do not start or install Docker as part of Task 4.

## Task 14 real-tenant validation

The four scenario manifests cover onboarding, role/capability boundaries, directory-role PIM and consent/B2B revocation. They are opt-in manual scripts, not destructive automation. Read `tests/RealTenant/README.md` and follow the scenarios in this order:

1. Establish the dedicated tenant fixtures and record only tenant/workspace/user/group/guest object IDs plus role assignment/template IDs.
2. Load `tests/RealTenant/real-tenant.env.example` into an ignored local file. Set `ATEA_REAL_TENANT_RUN=true` only for the interactive run and supply an HTTPS API base URL, tenant object ID and short-lived delegated API token. The scenario loader validates supplied object IDs and never emits the token, invitation nonce or raw Graph response.
3. Run onboarding with a platform-admin session for workspace/invitation setup, then switch to the customer-admin session for invitation redemption and delegated consent. Record `connected`, `permission_incomplete`, `consent_revoked` or another truthful connection state.
4. Run role/capability reads with Global Reader and User Administrator accounts. Global Reader must see permitted reads while mutation capabilities remain `read_only`/non-allowed; User Administrator lifecycle capabilities are allowed only when consent and tenant-wide role scope permit them. Keep any mutation as a separately approved manual action with a fresh idempotency key.
5. Read eligible-inactive, approval-required and MFA-required PIM fixtures. If activation is explicitly approved, complete it interactively and verify a later Graph-backed read before recording `active`; never silently activate or bypass approval/MFA.
6. Remove delegated consent and the Atea guest's customer-assigned role/membership manually in Entra, refresh the session, and verify `consent_revoked` or a clear missing-membership/role explanation. The scenario does not delete guests, revoke tokens or remove roles automatically.

### Evidence and cleanup

Evidence may contain scenario name, timestamp, object IDs, role assignment/template IDs, expected/observed safe states, HTTP status and safe correlation/request IDs. Do not attach access tokens, invitation URLs/nonces, email addresses, claims, browser storage, `Authorization` headers or raw Graph payloads. Clear the token and nonce variables immediately after the run. Deactivate temporary PIM assignments, cancel pending approval requests, remove temporary role assignments and restore the agreed tenant baseline manually; cleanup actions require customer-owner approval.
