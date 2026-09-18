# Task 4 test-tenant runbook

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
