# Real-tenant manual scenarios

These files are an opt-in manual scenario manifest for the dedicated, non-production Entra test tenant. The repository has no real-tenant test project, so the four C# files are compile-ready scenario skeletons rather than CI tests or an executable runner. Follow their `Steps` in order with the local API/UI and record the result in the approved test evidence location.

## Safety contract

- Use only a dedicated disposable tenant and test app registration. Never use production tenant IDs, users, tokens or consent.
- The harness is disabled unless `ATEA_REAL_TENANT_RUN=true`. `false` is the checked-in default.
- `RealTenantEnvironment.Load` requires an absolute HTTPS base URL, a tenant object ID and a short-lived API access token. It validates supplied records as GUID object IDs and never prints the token, invitation nonce or raw Graph response.
- Keep local environment values outside Git. The only tenant records permitted in the local secret store are tenant/workspace/user/group/guest object IDs and Entra role assignment/template IDs. Do not record names, email addresses, invitation URLs/nonces, bearer tokens, claims, raw Graph payloads or `Authorization` headers.
- Provisioning, lifecycle mutations, license/group changes, PIM activation, consent changes, guest removal and revocation are not automated. Every such action requires explicit customer-owner consent and an operator following the manual step in the scenario.
- A scenario may read connection health/capability state after a manual change. A successful request or submitted PIM request is not proof of an active role; refresh the Graph-backed state.

## Configure the process

Copy `real-tenant.env.example` to an ignored local file or load the same variables from the local secret store. Do not put the access token or invitation nonce in the example file, shell history, CI variables shared with other jobs, or test output.

```sh
set -a
. ./tests/RealTenant/real-tenant.env.local
set +a
```

Set `ATEA_REAL_TENANT_RUN=true` only for the one interactive run. Clear `ATEA_REAL_TENANT_ACCESS_TOKEN` and `ATEA_REAL_TENANT_INVITATION_NONCE` immediately afterwards. The existing `ATEA_REAL_ENTRA_*` variables used by `RealEntraValidationTests` are separate; do not mix those automated token checks with this manual scenario record.

## Required tenant fixtures

Create or verify these fixtures before testing, using separate cloud-only accounts where possible:

1. Customer administrator who can grant/revoke delegated consent.
2. Active Global Reader account for read-only capability checks.
3. Active User Administrator account for approved lifecycle checks.
4. Eligible inactive PIM account, with a disposable directory-role assignment.
5. Eligible PIM account whose policy requires approval.
6. Atea B2B guest with a customer-assigned active role.
7. Disposable test user and, if group boundaries are being exercised, disposable test group.

Record only the corresponding object IDs and role assignment/template IDs. Do not record the fixture names or email addresses in the tenant record.

## Scenario order

### Onboarding

Use `OnboardingScenario.Steps` to call the Atea-only `/api/platform/workspaces/onboard` endpoint once with the test tenant ID, workspace name, and nominated first-admin sign-in address. It creates the workspace and first-admin invitation atomically. Copy the one-time invitation URL once, redeem it using the nominated admin's federated Entra sign-in, then confirm the workspace setup view loads even before Graph consent is configured. Start delegated consent and check `/api/workspaces/current/connection-health/check`; the expected healthy state is `connected`. Remove consent manually in Entra and repeat the read-only check; the expected state is `consent_revoked`. The scenario must never claim connected after consent is removed.

The invitation URL/nonce is a credential. Copy it once through the approved secure channel, use it once, and do not save it in evidence.

### Role and capability boundaries

Use `RoleCapabilityScenario.Steps` with the recorded Global Reader and User Administrator object IDs. `/api/capabilities` must show read access for Global Reader while mutation capabilities remain `read_only` or another explicit non-allowed state. User Administrator may show approved lifecycle capabilities as `allowed` only when delegated consent, role scope and policy permit it.

Do not automatically call `POST /api/users`, `PATCH /api/users/{userObjectId}`, `/disable`, group, license or other mutation routes. If a customer owner approves one disposable lifecycle check, perform it manually through the UI, send a fresh `Idempotency-Key`, and record only the outcome, capability state and safe correlation IDs.

### PIM

Use `PimScenario.Steps` to read `/api/users/{userObjectId}/pim` for an eligible inactive role, an approval-required role and an MFA-required policy. Expected states include `eligible_inactive`, `approval_required`, `mfa_required`, `activation_pending`, `active`, `policy_blocked` and `temporarily_unavailable` as applicable.

Activation through `POST /api/pim/activations` is a privileged manual action. Require explicit confirmation, justification, a bounded duration, a fresh idempotency key and the tenant's interactive MFA/approval policy. Never treat a submitted request as active until a subsequent Graph-backed read says `active`. Unsupported Azure resource-role and group-PIM paths must remain safe non-success states.

### Revocation and B2B

Use `RevocationScenario.Steps` to establish a connected baseline, manually remove delegated consent, check for `consent_revoked`, restore consent through the customer-admin handoff, and then manually remove the Atea guest's customer role or membership. After a fresh session/refresh, `/api/capabilities` and connection health must explain the missing membership, role or consent rather than silently retaining access.

Revocation is intentionally a human-confirmed tenant-admin action. Do not automate role removal, guest deletion, token revocation or session changes.

## Evidence and cleanup

Capture only: scenario name, timestamp, tenant/workspace/user/group/guest object IDs, role assignment/template IDs, expected and observed safe state, HTTP status, and correlation/request IDs that contain no token material. Redact response bodies before sharing evidence. Never attach browser storage, network `Authorization` headers, access tokens, invitation URLs, raw Graph payloads or PII.

After the run, deactivate temporary PIM assignments, cancel pending approval requests, remove temporary role assignments and restore the tenant to its agreed baseline in the Entra admin center. Clear the local token and nonce variables. Do not delete shared tenant objects unless the customer owner explicitly approved that cleanup.
