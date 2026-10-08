# Dedicated Entra test-tenant runbook

This runbook covers local onboarding and opt-in real-tenant consent-first
acceptance. Use only a dedicated, non-production customer tenant and the
separate existing Atea-owned development/test API and customer SPA
registrations. The checked-in scenario manifest is under `tests/RealTenant/`;
these scenarios are intentionally not part of CI.

## Local Atea platform-admin demo

The local Atea administrator is a Development-only provider for the onboarding demo. It is not customer authentication, does not grant Microsoft Graph permissions, and must not be enabled outside a local Development API. Configure all six keys in the environment used by the API:

```text
AteaAdmin__LocalDevelopment__Enabled=true
AteaAdmin__LocalDevelopment__Username=local-admin
AteaAdmin__LocalDevelopment__Password=change-me-locally
AteaAdmin__LocalDevelopment__ObjectId=00000000-0000-0000-0000-000000000001
AteaAdmin__LocalDevelopment__DisplayName="Local Atea Administrator"
AteaAdmin__LocalDevelopment__AllowAllWorkspaces=true
```

`AllowAllWorkspaces` is intentionally a local limitation for the demo. Production and future Atea Entra federation require explicit workspace scope; never reuse the sample password or enable this gate in a deployed environment.

Configure the local onboarding URLs and development signing key in the API environment:

```text
Onboarding__CustomerClientId=<customer-SPA-client-id>
Onboarding__ApiApplicationIdUri=api://<API-client-id>
Onboarding__PublicBaseUrl=http://localhost:5173
Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
Onboarding__ConsentSigningKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
PlatformAuthorization__RequiredScope=platform.admin
```

Copy `.env.example` to `.env`; it contains the local Development admin credentials and onboarding values. Start the local stack in exactly three terminals, sourcing `.env` in each terminal (the API waits for PostgreSQL in Compose via its health dependency). Terminal 1:

```bash
set -a; . ./.env; set +a
docker compose up -d postgres
docker compose ps postgres
```

Terminal 2:

```bash
set -a; . ./.env; set +a
dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj --urls http://localhost:8080
```

Terminal 3:

```bash
set -a; . ./.env; set +a
cd src/Web && npm ci && npm run dev -- --host 0.0.0.0 --port 5173
```

For the all-in-Compose path, run `set -a; . ./.env; set +a; docker compose up --build`. Compose supplies `ASPNETCORE_ENVIRONMENT=Development`, the local admin credentials, all `Onboarding__*` values, and the container PostgreSQL connection string. `Onboarding__PublicBaseUrl=http://localhost:5173` is required for local invitation links; the rejected placeholder `https://workplace.example` is never a runtime fallback. After testing, use `docker compose down`; use `docker compose down -v` only to intentionally discard the local PostgreSQL volume.

Open `http://localhost:5173/admin`, sign in with the configured local Atea credentials, create a workspace using the verified tenant domain or GUID, open the workspace detail page, nominate the customer administrator by Entra object ID, and create an invitation. The operator chooses the tenant explicitly; do not infer it from the administrator's email domain. Platform bearer routes require the configured `platform.admin` value in the space-delimited `scp` claim; the Development-only local admin cookie remains usable without that bearer claim. A customer bearer token with a matching object ID but no platform scope must still be rejected before `/api/platform/*`. Copy the one-time invitation instruction only through the approved handoff channel; do not store it in notes, logs, screenshots, or test output. The workspace detail page should show membership and invitation status metadata without exposing nonce/hash fields.

The route boundary is deliberate. `/admin` uses the local Atea cookie session for platform onboarding. Customer routes use the customer tenant's Entra sign-in and effective Microsoft Graph permissions. The customer administrator redeems the invitation in the customer tenant, completes delegated consent, and continues through the customer-facing application. A local Atea admin session cannot be used to call customer directory routes.

## Customer-admin handoff

1. An authorized Atea platform administrator provisions the workspace and creates an invitation instruction for the nominated customer administrator.
2. The instruction is copied once from the API response and handed to the customer administrator through the agreed customer channel. The API does not send email, log the URL or store the nonce.
3. For an eligible administrator invitation, the customer opens the link while signed out, reviews the workspace and delegated permission summary, and starts tenant-specific admin consent. Entra returns to the configured public callback. The callback tenant hint or `admin_consent` query value is not trusted as proof of identity or grant.
4. The consent transaction is held in that browser tab's `sessionStorage`. The customer returns to the same configured primary origin, signs in at the server-returned tenant authority as the nominated user, and redeems the invitation. The API checks verified token tenant/object identity, invitation lifecycle and one-time-use state before creating membership.
5. Invitation completion automatically checks baseline delegated access and configured scope coverage. A healthy result continues to the overview; missing or unknown scopes remain a partial/retryable state. A health-check retry is a new check, not a replay of consumed consent state.
6. Ordinary `member` invitations retain the authenticated sign-in/redemption route and do not expose anonymous consent start. Legacy authenticated workspace re-consent remains available to members.

The anonymous preview does not reveal the invitee address, tenant/workspace IDs,
membership details or connection diagnostics. Anonymous consent does not
create membership or change connection state. A consent challenge is
single-use, tenant/workspace/invitation-bound, and expires after at most
20 minutes or at invitation expiry. A lost redemption response can be recovered
only by the same authenticated identity with its still-valid challenge; ordinary
nonce-only replay remains rejected.

## Atea B2B guest completion path

1. Invite the Atea operator as a B2B guest in the customer tenant and complete redemption in the customer tenant browser session.
2. Assign only the customer-approved directory role needed for the test case (normally Directory Readers; use Privileged Role Administrator only for role/PIM scenarios).
3. For legacy member access, start delegated consent from workspace settings, complete customer admin consent, then run a connection check. This remains the API-client/Graph-`/.default` path; the consent-first administrator invitation instead targets the customer SPA using the API resource's configured `/.default` URI.
4. Validate the expected state: `connected`, `permission_incomplete`, `consent_revoked` or `temporarily_unavailable`. Revoke consent and repeat the check to verify the revoked path.

Record only tenant/object identifiers, granted scope names, connection state and correlation IDs in test evidence. Never record access tokens, invitation nonces or raw authorization headers.

## Deterministic test and Docker requirements

The API integration suite includes Testcontainers PostgreSQL migration,
atomic-consume and race coverage. Docker/Testcontainers execution requires a
running Docker daemon. When unavailable, record the failure or exact skip
reported by the runner; do not substitute an in-memory database for PostgreSQL
semantics.

## Opt-in fresh-tenant acceptance

The four scenario manifests cover onboarding, role/capability boundaries,
directory-role PIM and consent/B2B revocation. They are opt-in manual scripts,
not destructive automation. Read `tests/RealTenant/README.md` and follow the
scenarios in this order. For the first onboarding acceptance, use a dedicated
organizational tenant with both customer service principals absent and user
consent disabled. Use an active tenant administrator with a role that can grant
tenant-wide delegated consent (typically Cloud Application Administrator or
Application Administrator; Global Administrator or Privileged Role
Administrator also suffices). Tenant policy, Conditional Access and PIM can
add requirements. Register the customer SPA `/auth/callback` (SPA platform) and
`/onboarding/consent/callback` (Web platform) on the customer SPA registration
at the exact configured primary origin. Keep the separate platform-admin SPA
and its authority out of customer consent.

1. Establish the dedicated tenant fixtures and record only tenant/workspace/user/group/guest object IDs plus role assignment/template IDs.
2. Load `tests/RealTenant/real-tenant.env.example` into an ignored local file. Set `ATEA_REAL_TENANT_RUN=true` only for the interactive run and supply an HTTPS API base URL, tenant object ID and short-lived delegated API token. The scenario loader validates supplied object IDs and never emits the token, invitation nonce or raw Graph response.
3. Run the registration script with the expected Atea home tenant, existing API/customer-SPA IDs, API application-ID URI and exact primary-origin callbacks. Review its sanitized dry-run before any separately authorized `--apply`; it never grants admin consent or modifies a customer tenant.
4. Run onboarding with a platform-admin session for workspace/invitation setup. Open the invitation in a fresh, signed-out browser tab, verify there is no pre-consent API sign-in, and complete the single admin-consent handoff before signing in as the nominated customer user. Confirm both customer service principals and expected delegated permissions appear; then verify exactly one redemption and automatic health/scope results.
5. Run denial, non-admin, wrong-tenant/account, invitation expiry/reissue, missing configured scope and revoked-consent cases. Verify legacy post-sign-in re-consent remains functional.
6. Run role/capability reads with Global Reader and User Administrator accounts. Global Reader must see permitted reads while mutation capabilities remain `read_only`/non-allowed; User Administrator lifecycle capabilities are allowed only when consent and tenant-wide role scope permit them. Keep any mutation as a separately approved manual action with a fresh idempotency key.
7. Read eligible-inactive, approval-required and MFA-required PIM fixtures. If activation is explicitly approved, complete it interactively and verify a later Graph-backed read before recording `active`; never silently activate or bypass approval/MFA.
8. Remove delegated consent and the Atea guest's customer-assigned role/membership manually in Entra, refresh the session, and verify `consent_revoked` or a clear missing-membership/role explanation. The scenario does not delete guests, revoke tokens or remove roles automatically.

Consent-first acceptance must run on the configured primary origin. The customer
callback cannot use a candidate hostname: the in-flight transaction is scoped
to that tab's `sessionStorage`, and pending state is never copied between
origins. Do not interpret the jsdom E2E suite, registration dry-run, or fake CLI
tests as a real browser or live Entra acceptance.

### Evidence and cleanup

Evidence may contain scenario name, timestamp, object IDs, role assignment/template IDs, expected/observed safe states, HTTP status and safe correlation/request IDs. Do not attach access tokens, invitation URLs/nonces, email addresses, claims, browser storage, `Authorization` headers or raw Graph payloads. Clear the token and nonce variables immediately after the run. Deactivate temporary PIM assignments, cancel pending approval requests, remove temporary role assignments and restore the agreed tenant baseline manually; cleanup actions require customer-owner approval.
