# Consent-first customer onboarding design

**Date:** 2026-10-07

**Status:** Design direction for the authorized unattended design → plan → implementation workflow. This commit contains the specification only; it is not implementation or live-tenant validation.

**Scope:** Atea-invited customer onboarding using delegated Microsoft Graph permissions, the existing customer SPA/API registrations, and one tenant-admin consent handoff.

## Outcome and intent

An authorized Atea operator should create a customer workspace without asking the customer to find a tenant GUID. The nominated customer administrator should open one invitation link, approve the Microsoft Entra consent screen before application sign-in, and reach a verified workspace without manually registering an enterprise application or pressing a separate connection-check button.

The original request asks whether an app can add itself to a tenant when an administrator grants consent, requests research and brainstorming, and authorizes planning and execution afterward. Entra provisions the customer service principals during consent; the application does not create them using a bootstrap Graph permission.

Success means:

- A fresh organizational tenant with user consent disabled can onboard without first obtaining an API token.
- One consent screen covers the customer SPA → API delegated grant and the API → Graph delegated grants configured by Atea.
- Only the invited, authenticated customer identity gains membership. Anonymous consent neither creates membership nor proves connection.
- Connection status reflects actual delegated access, including incomplete or unavailable verification.
- Existing GUID provisioning, invitation redemption, customer authorization, platform-admin isolation, manual connection checks and post-sign-in re-consent remain supported.

## Assumptions

1. The customer SPA and API registrations are owned by Atea in the same home tenant, as required for `knownClientApplications`. Development and hosted environments use separate registration pairs.
2. The Microsoft public cloud is the supported authority. Sovereign-cloud authorities and personal Microsoft accounts are not included.
3. Atea still approves customers and hands invitation instructions through its existing approved channel. This feature does not send email or create customer workspaces anonymously.
4. The nominated first administrator can perform tenant-wide delegated consent. Cloud Application Administrator or Application Administrator normally suffices for this delegated-only permission set; Global Administrator or Privileged Role Administrator also works. Tenant policy, Conditional Access and PIM can impose additional requirements.
5. The consenting administrator may be different from the invited user. Consent does not bind an identity to our workspace. The post-consent sign-in must use the nominated identity, with the existing approved object-ID or invited-address check.
6. The full current `GraphScopeCatalog.CapabilityEvaluationScopes` is the version-one registration permission set, including optional-module scopes. This matches the researched bundled-consent direction but is a broad delegated set. The landing page must disclose it. Consent does not enable disabled modules or assign Entra roles.
7. The user is unavailable for questions. The decisions below resolve ambiguity explicitly. Subsequent implementation planning should follow this specification, not add self-service signup or another trust model.
8. `knownClientApplications` behavior has documentation support but has not been proven with the actual registrations in a fresh customer tenant. The dedicated-tenant acceptance check is a rollout gate, not evidence supplied by this design.

## Existing baseline

`WorkspaceEndpoints` provisions a tenant-GUID workspace and an administrator invitation. `InvitationService` generates a 32-byte random nonce and stores only its SHA-256 hash. `WorkspaceOnboardingRepository` redeems it transactionally after checking tenant, invited address or approved object ID, expiry, revocation and one-time use.

The SPA currently wraps invitation and consent callback routes in `AuthProvider`, which demands sign-in before rendering children. That sign-in requests the API's `access_as_user` scope. In a fresh tenant the resource service principal might not exist, and user consent can be blocked.

Authenticated `/api/workspaces/current/consent/start` currently targets the API client ID with `https://graph.microsoft.com/.default`. `ConsentChallengeService` signs a ten-minute challenge bound to workspace and tenant; the repository atomically consumes its hash. Completion validates state but does not verify Graph access. The callback component then calls the manual connection-check endpoint.

`ConnectionHealthReader` probes only `User.Read` through delegated OBO and `/me`. A successful baseline check is not evidence that every catalog permission was granted. The current onboarding transition table also rejects an initial `consent_required → permission_incomplete` or unavailable result. Both limitations must be handled without fabricating a successful connection.

## Approaches considered

| Approach | Advantages | Trade-offs |
| --- | --- | --- |
| **Consent-first invitation + bundled SPA/API consent (chosen)** | Removes the bootstrap sign-in dependency; preserves Atea invitations and delegated RBAC/PIM; one administrator approval provisions both customer service principals. | Requires correct multi-tier registration configuration, an anonymous invitation surface, and tab-safe callback orchestration. |
| API-first consent followed by separate SPA consent | Reuses the existing API consent target with little registration change. | The customer can still face a second consent prompt or fail SPA sign-in under restrictive policy. Does not reliably satisfy the one-approval goal. |
| Fully self-service tenant signup | No Atea provisioning handoff; lowest sales/onboarding friction. | Changes customer governance, tenant admission and abuse controls. Unnecessary for fixing permission onboarding. |

Choose the first approach, corresponding to research Option B. Do not add app-only access to avoid a consent prompt: it changes standing authority and bypasses the existing signed-in-user role/PIM model.

## Architecture and focused responsibilities

Retain .NET minimal API, EF/PostgreSQL, React/Vite, MSAL and server-side Graph OBO. Add or extend only the following boundaries:

| Unit | Purpose and interface | Dependencies |
| --- | --- | --- |
| `ITenantResolver` / `OidcTenantResolver` | `ResolveAsync(string domain, CancellationToken)` returns a canonical tenant GUID or a typed invalid/unavailable result. Never authorizes a user. | Fixed-host `HttpClient`, bounded memory cache; deterministic fake in tests. |
| Invitation read boundary | `FindByNonceHashAsync` returns internal invitation/workspace metadata. `InvitationService` exposes minimal preview and eligibility checks. | Existing invitation repository; no raw-nonce persistence. |
| `InvitationConsentService` | Starts an invitation-bound challenge and resolves a callback to a server-held tenant/sign-in instruction. It checks invitation lifecycle, not membership authorization. | Invitation read boundary, challenge signer/repository, validated onboarding options. |
| `ConsentChallengeService` | Signs/parses versioned invitation state; preserves legacy state parsing. Signature validation precedes use of decoded fields. | Existing HMAC key and hash-based persistence. |
| `IWorkspaceConnectionVerifier` | `VerifyAsync(WorkspaceContext, includePermissionCoverage, CancellationToken)` runs the baseline check, optionally fresh scope coverage, maps safe results and records health. | Existing health reader, extracted delegated scope probe, onboarding service. |
| `IDelegatedScopeAvailabilityReader` | Probes effective availability of supplied scope names without directory-role reads. Returns available, missing-consent or unknown outcomes per scope. | Existing delegated client factory and token-error mapper. Extract the scope-probing logic currently private to `GraphAuthorizationSnapshotReader`. |
| Public invitation/callback pages | Render setup instructions before sign-in and orchestrate the tab's pending invitation through MSAL and redemption. | Same-origin anonymous API client; MSAL only when authentication is needed. |

Endpoint handlers translate results; they do not duplicate invitation rules or Graph-health mapping. Existing capability evaluation remains authoritative for individual operations. Do not use the role-reading authorization snapshot as a consent verifier: inability to read a role assignment must not be mislabeled as absent tenant consent.

## Tenant input and resolution

Extend both existing platform creation requests with optional `tenantDomain`; retain the existing `tenantId` field and GUID-only callers. Exactly one nonempty input is accepted. `Guid.Empty`, two inputs, invalid domain syntax or a missing input produce `400 invalid_tenant_input`.

The form label becomes **“Tenant domain or ID”**, accepting `contoso.com`, `contoso.onmicrosoft.com` or a GUID. It submits the appropriate field rather than sending a domain as `tenantId`. Display name and first-admin fields retain current validation. An email domain is not automatically inferred as the tenant: the operator chooses the tenant explicitly.

For a domain:

1. Trim, lowercase and normalize valid DNS/IDN names to ASCII. Reject URLs, ports, user-info, paths, query strings, fragments, IP addresses, wildcards and reserved `common`, `organizations` or `consumers` authority names.
2. Request only `https://login.microsoftonline.com/{escaped-domain}/v2.0/.well-known/openid-configuration`. Disable redirects, set a five-second timeout and a 64-KiB response limit.
3. Require the issuer to have exactly the trusted HTTPS authority and a nonempty GUID tenant segment followed by `/v2.0`. Require the authorization/token endpoints to use that same host and tenant. Reject generic/templated issuers and inconsistent metadata.
4. Return only the GUID. Cache successful mappings for one hour; do not cache failures. Discovery failure returns `422 tenant_domain_not_found`; timeout/upstream outage returns `503 tenant_resolution_unavailable`, with guidance to retry or enter a verified GUID.

GUID input preserves existing behavior and does not require discovery. Resolve before the provisioning transaction. Duplicate prevention continues to use the canonical GUID, so aliases cannot create duplicate workspaces. Store no domain mapping in PostgreSQL; the platform response already displays the resolved tenant ID. OIDC discovery identifies a directory, not proof of domain ownership or operator/customer authority.

## Customer sequence

1. The authorized operator creates/onboards the workspace by domain or GUID. Existing provisioning stores the canonical tenant ID, `awaiting_invitation` state, nominated administrator and invitation.
2. `/invitations/{nonce}` opens anonymously. Preview shows the workspace name, setup steps, delegated permission summary, exact permission details and required administrator role. It does not request a token or load workspace/session APIs.
3. Clicking **“Connect your Microsoft 365 tenant”** starts consent. The SPA stores a pending transaction in this tab's `sessionStorage` before navigating to Microsoft.
4. The API builds a tenant-specific admin-consent URL targeting the **customer SPA client ID**, with **`scope=api://{api-client-id}/.default`** for the API's configured application ID URI. This is not bare `.default` or Graph `.default` on the SPA. The API registration lists the customer SPA in `knownClientApplications`, and the SPA's static required permissions include API `access_as_user`. Entra bundles the resource API's Graph requirements and provisions both service principals.
5. Microsoft returns to `/onboarding/consent/callback`. The public callback verifies the tab's expected state and asks the anonymous resume endpoint to validate the signed challenge and database binding. It does not trust query-string `tenant` or `admin_consent=True` as identity or a grant.
6. On a non-error callback, MSAL starts sign-in against `https://login.microsoftonline.com/{server-returned-tenant-id}` using API `access_as_user`, `/auth/callback`, and account selection. It returns to `/invitations/{nonce}` using the existing redirect-start-page pattern. The page says to use the invited account, without exposing that address anonymously.
7. After token acquisition, the SPA automatically redeems the invitation through the existing authenticated endpoint. The existing token `tid`/`oid` and invited-identity rules apply. It does not load the workspace shell before redemption succeeds.
8. The SPA then calls authenticated `/api/workspaces/current/consent/complete` with the original state. The server checks the redeemed invitation, caller and workspace binding, atomically consumes the state, and automatically verifies baseline and permission coverage.
9. Show a truthful result. A connected customer continues to `/overview`; partial/unavailable results show actionable scope or retry guidance and a link to the permission-aware overview. A retry uses a fresh health check, never replays the consumed state.

Invitations for `customer_admin` and `workspace_owner` are eligible for consent-first setup. Ordinary `member` invitations retain sign-in/redemption behavior and do not expose anonymous consent start. Preview returns a safe `flow` discriminator (`consent_first` or `sign_in`) rather than internal role/module assignments.

The existing authenticated consent-start endpoint remains API-client/Graph-default based for re-consent and does not require an invitation. Its callback uses the same public route but the legacy pending-transaction discriminator. No customer grant is requested for the platform-admin SPA.

## API contracts

All anonymous routes explicitly use `AllowAnonymous`, overriding the existing fallback policy. The existing invitation namespace is already excluded from membership resolution; preserve that narrow exception. `/api/platform/*` policy, JWT issuer/audience validation and authenticated workspace resolution are unchanged.

| Endpoint | Contract and effects |
| --- | --- |
| `POST /api/platform/workspaces` and `/workspaces/onboard` | Add optional `tenantDomain` mutually exclusive with `tenantId`. Existing success response, operator checks, audit and GUID behavior remain. |
| `GET /api/invitations/{nonce}/preview` | Anonymous. Valid live invitation returns `{ workspaceName, expiresAt, flow, permissionScopes }`. No tenant ID, workspace/invitation IDs, invitee identity, object IDs, membership/module details or connection diagnostics. Scope names are public catalog data. No lifecycle mutation. |
| `POST /api/invitations/{nonce}/consent/start` | Anonymous, empty JSON body. Requires an unexpired, unrevoked, unredeemed eligible invitation. Returns existing-shaped `{ authorizationUrl, scopes, challenge, correlationId, expiresAt }`. Only server configuration determines tenant, client and redirect URI. Persists a state hash, not the URL or nonce. |
| `POST /api/invitations/{nonce}/consent/resume` | Anonymous, body `{ state, tenant?, errorCode? }`. Signature + hash + invitation binding/lifecycle validation without consuming the challenge. Returns `{ valid, status, tenantId?, correlationId }`; tenant ID only for a valid successful resume. Status is `ready_to_sign_in`, `consent_denied` or `invalid_callback`. It does not set consent or connection state. |
| `POST /api/invitations/{nonce}/redeem` | Remains authenticated; unchanged success shape. Record the redeeming object ID transactionally. For recovery after a lost response, a replay by exactly that already-recorded identity can retrieve the existing success result only when supplying its still-valid invitation challenge as an optional body field. Ordinary nonce-only replay remains invalid. No second membership or redemption audit is created. |
| `POST /api/workspaces/current/consent/complete` | Remains authenticated. Retain `{ state, tenant, errorCode? }`, `valid`, `status`, `correlationId`. For invitation state, success additionally returns `health` and `permissionCoverage`; consume once and run verification. Legacy states retain `consent_received`/`consent_denied` and the legacy frontend's separate automatic check. Invalid state does not run Graph. |
| `POST /api/workspaces/current/connection-health/check` | Existing baseline semantics remain the default. Optional `{ includePermissionCoverage: true }` runs the same comprehensive verifier for retry and returns additive coverage fields. No consent-state replay or invitation is needed for an authenticated member's check. |

Preview/start return the same `404 invitation_unavailable` for malformed, unknown, expired, revoked or redeemed nonces. Ineligible live invitations return `409 invitation_consent_not_available` only to their nonce holder. Database/configuration outage returns a sanitized `503`; it must not be treated as expiry. Resume uses generic invalid-callback results rather than revealing which binding failed.

For invitation completion, `status=consent_received` means the callback was processed, not “connected”; `health.status` is the connection result. A wrong callback tenant hint can reject the callback, but can never select a workspace or change the authoritative tenant. Legacy tenant mismatch behavior is retained. A denied legacy callback still consumes its challenge and returns the existing safe denial result.

## State binding, persistence and recovery

Extend the existing challenge format rather than changing every consent caller:

- Preserve validation of existing five-field legacy workspace challenges and their ten-minute lifetime.
- Use a versioned `invitation` payload containing purpose, correlation ID, workspace ID, canonical tenant ID, invitation ID, expiration and fresh random bytes. Never include raw invitation nonce, email, user claims or secrets in signed state.
- Sign with the existing HMAC-SHA256 key and fixed-time signature comparison. Bound state length and parse fields strictly before database access; reject unsupported purpose/version and malformed timestamps safely.
- Invitation challenge lifetime is **20 minutes or invitation expiry, whichever is earlier**. Expiry includes the consent/sign-in/redemption journey; an expired challenge requires a new start.
- Store `SHA-256(state)` and server-held bindings. The signed payload is not encrypted and must not be treated as confidential.

Add one additive EF migration:

1. `ConsentChallenges.InvitationId` nullable foreign key with restricted delete, and `Purpose` defaulting to `workspace` for existing records. Index invitation ID plus expiry for lookup. Keep hash as the unique primary key.
2. `PlatformInvitations.RedeemedByTenantObjectId` nullable. New redemptions write it in the same atomic transaction as `RedeemedAt` and membership creation. Leave historical records null: they cannot use invitation-specific retry, but existing memberships and legacy re-consent still work.

Resume hashes the supplied nonce, locates the invitation and compares its ID/workspace/tenant with both verified payload and stored challenge. It accepts only a pending unconsumed, unexpired challenge. If the invitation was redeemed during this flow, resume may still return the minimal sign-in instruction while the challenge remains valid; only authenticated completion/recovery can prove the redeemer.

Authenticated invitation completion requires the resolved token tenant/workspace to match the challenge, the invitation to have been redeemed by this token's object ID, no revocation, and unexpired invitation and state. Membership alone is insufficient. Atomically consume the challenge with all these predicates enforced transactionally; invitation revoke/reissue races must fail closed. Only one concurrent completion starts verification.

Preview/resume never mark an invitation redeemed or a challenge consumed. Failed or denied anonymous callback does not change persistent onboarding state; starting again creates a new random challenge. Old unexpired challenges need not be invalidated when starting another tab. Normal completion consumes only its own challenge; revocation/reissue invalidates all linked challenges through invitation validity checks. An allowed reissue must revoke the replaced invitation in the same transaction as replacement creation, rather than depend only on the existing unredeemed-email bulk revocation.

Tab state contains the nonce, expected challenge, expiry, flow kind and pending step, scoped to this tab and cleared after completion or terminal failure. The callback must exactly match the stored challenge before resume or MSAL. Reject callbacks opened in another tab or without pending state with “Open your original invitation and try again.” Do not reconstruct a usable nonce from callback state. Keep a pending step across MSAL redirects, remove callback query parameters with `history.replaceState`, and never put these values in analytics or local storage.

After a completion response is lost, a valid member may call health check; a consumed state must still return invalid on replay. After redemption response loss, the optional challenge-bound same-redeemer recovery allows progression without weakening normal single-use invitations. After challenge expiry following redemption, direct the authenticated member to workspace settings for legacy re-consent; do not restore anonymous invitation privileges.

## Automatic verification and truthful state

Use the existing `IConnectionHealthReader` and `/me` delegated probe for baseline health. Reuse the extracted scope-availability reader for fresh, uncached catalog scope probes after consent. Probe individual scopes using OBO, not Graph directory mutations, app-only calls or tenant-wide grant enumeration permissions. Limit scope-probe concurrency to four and bound comprehensive verification to 30 seconds; dispose leases and pass cancellation.

Return `permissionCoverage = { availableScopes, missingScopes, unknownScopes }`. Scope names must come from the catalog. Classify missing only when token acquisition positively reports missing consent; Conditional Access/MFA, temporary faults or unclassified failures are unknown, with safe problem categories, not proof of absent consent. Effective OBO availability is not an inventory of tenant-wide grants and does not prove directory-role authority.

For comprehensive checks:

- Baseline failure preserves the existing reader's consent-required, revoked, failed or unavailable category.
- Baseline success with all catalog scopes available yields `connected`.
- Baseline success with definite missing scopes yields `permission_incomplete`, even if other scopes are unknown. Show unknown checks separately.
- Baseline success without definite missing scopes but with unknown probes yields `temporarily_unavailable`. Do not claim full coverage.

Persist only safe state, available scope names, failure category and verification timestamp; return exact missing/unknown scope names to the authenticated caller. Capabilities still evaluate roles, modules and PIM separately. Label the result as this signed-in user's verification, not an irrevocable tenant-wide guarantee. A baseline-only manual check retains its current “baseline connection” meaning and must not advertise complete catalog coverage.

Extend the transition table deliberately: verification may move from any post-redemption state to any verification result (`consent_required`, `connected`, `permission_incomplete`, `temporarily_unavailable`, `consent_revoked`, `connection_failed`). `awaiting_invitation` still only moves to `consent_required` through redemption and cannot jump directly to connected. Change invitation redemption to set `consent_required` only when the workspace was `awaiting_invitation`, so a later invitation does not reset an existing connection.

A check updates `LastVerifiedAt` only after recording an observed result; never update it on consent start, callback receipt or redemption. A transient probe outcome is a checked-but-unavailable observation, not successful verification; UI wording must distinguish them. Do not transition temporarily through `connected` to get around the current state machine.

## SPA routing and copy

Modify the entrypoint boundary in `main.tsx`: public invitation and consent-callback routes render outside `AuthenticatedContent` and before workspace session/capability loading. Retain an MSAL-provider boundary that initializes and handles redirect transactions without forcing public-route sign-in. The authenticated customer shell and separate `/admin` provider retain existing guards.

Keep existing route URLs. Add a focused `InvitationLandingPage` and pending-flow helper; retain `InvitationRedemptionPage` for normal invited-member behavior and compatibility. Evolve `ConsentCallbackPage` to dispatch invitation versus legacy callbacks using the tab's stored flow, not decoded URL claims. Update `AuthProvider` actions to accept a server-returned tenant authority for this journey and pin both redirect and silent API-token requests to the selected account/tenant. Do not rely on `accounts[0]` when a cached account belongs to another tenant.

Update `OnboardingPage`'s existing authenticated consent-start action to save a `workspace` pending transaction before following its consent URL. It records the returned challenge and the trusted endpoint URL's expected tenant, but no invitation nonce. On the shared callback route, this branch authenticates the existing member as necessary and uses the existing complete-then-check sequence. In-flight legacy callbacks created before this release may lack tab state; offer authenticated workspace-settings re-consent instead of trusting an unsolicited callback.

Core copy:

- Title: **“Connect {workspaceName} to Microsoft 365”**
- Explanation: **“An administrator approves Atea's delegated permissions in Microsoft. Microsoft adds the enterprise applications to your tenant. Then sign in with the account invited by Atea.”**
- Role guidance: **“Use an active Cloud Application Administrator, Application Administrator, Privileged Role Administrator or Global Administrator account. Your tenant may require MFA or PIM activation.”**
- Boundary: **“Consent does not assign Microsoft 365 roles. Actions remain limited by your signed-in account, workspace access and tenant policy.”**
- Primary action: **“Connect your Microsoft 365 tenant”**
- After callback: **“Permissions approved. Sign in with your invited account to finish setup.”** This describes Microsoft's reported result; follow with **“We will verify access after sign-in.”**
- During verification: **“Checking delegated access…”**
- Partial result: **“Connected to Microsoft, but some delegated permissions are unavailable.”** List definite missing scopes and a re-consent action; unknown checks get retry guidance.
- Recovery: **“Your invitation is no longer available. Ask Atea for a new invitation.”**

Use the existing Atea logo, bundled Inter, semantic themes and compact portal typography. Keep a single neutral setup panel, green primary action, expandable permission details and text-labelled states. No new design system or decorative wizard. Preserve keyboard focus, labelled buttons, status/alert announcements, light/dark contrast and narrow-screen reflow. Move focus to the result/error heading after an asynchronous stage.

## Failure handling

| Condition | User-facing behavior | Server effect |
| --- | --- | --- |
| Consent denied/cancelled | Explain that no workspace access was granted; offer a new consent attempt from the original link. Do not automatically sign in or redeem. | No membership, consent-state or health mutation. |
| Not-admin / AADSTS privilege error | Ask for an active eligible admin role, PIM activation or customer IT help. Do not suggest granting oneself a role. | Safe denial category only. |
| Application not found / invalid resource / redirect mismatch | Configuration guidance to contact Atea with correlation ID. | No health success; check registration configuration. |
| Wrong callback tenant | Invalid callback; restart from original invitation. | No consume or authorization from the hint. |
| Wrong signed-in tenant or identity | Offer tenant-pinned account selection and retry; show invited-account guidance without revealing invitation data. | Redemption denied; no membership or challenge consumption. |
| Invalid, expired, revoked or reused invitation | Generic unavailable page; ask Atea for reissue. | No mutation except a legitimate prior redemption. |
| Expired/tampered/consumed state or missing tab transaction | Restart invitation if still live; after redemption use authenticated re-consent. | No Graph call or state reuse. |
| Graph partial permission coverage | Exact missing scope names, role/PIM boundary explanation and re-consent action. | Record `permission_incomplete`, not connected. |
| Propagation delay, MFA/Conditional Access, throttling or outage | Safe category and retry; interactive sign-in where required. Keep completed membership; offer overview. | Record truthful unavailable/category result; no silent consent/MFA/PIM bypass. |

Allowlist known provider error codes for guidance and discard raw `error_description`, claims and upstream exception text. AADSTS numbers may refine safe copy but never authorize the callback. Verification retry is explicit; do not build polling, a job queue or a background onboarding workflow.

## Security controls

- Add named ASP.NET rate-limit policies to preview/start/resume: 20 requests/minute per trusted client IP across anonymous onboarding, with start additionally limited to five/minute/IP; no queue. Add a 200/minute/instance aggregate cap and `429` with `Retry-After`. Configure forwarded-header trust explicitly; never use arbitrary client `X-Forwarded-For`. Apply equivalent edge limits before multi-replica production use. Process-local limits are abuse reduction, not a distributed security guarantee.
- Reject oversized nonce/state/body input before signing or querying. Nonces retain the existing 43-character base64url, 256-bit-random format; signatures/hashes are never logged.
- Same-origin JSON POSTs for start/resume; reject cross-origin browser `Origin`, do not enable wildcard CORS, and accept no caller-controlled return URL. A bearer nonce is not a user identity. Preview GET is read-only.
- Use `Cache-Control: no-store` on invitation APIs and sensitive landing/callback responses, `Referrer-Policy: no-referrer`, no third-party telemetry/resources on these pages, and scrub invitation path segments/callback query strings from HTTP/access logs and error reporting. Raw URLs must not become audit metadata.
- No callback creates membership, selects a customer context or declares connection using unsigned `tenant`, `scope`, `admin_consent`, `error` or an arbitrary redirect URI. Browser MSAL state/nonce/PKCE stays separate from the admin-consent challenge.
- Hosted callback/public URLs require HTTPS, exact configured origin/path, no user-info/query/fragment, and registered redirect URIs. HTTP is limited to explicit localhost Development. Validate `Onboarding:CustomerClientId`, consent signing and application-ID URI before enabling anonymous start. A missing legacy consent configuration should still fail safely rather than break unrelated test/dev routes.
- Safe audit records include workspace/invitation identifiers, purpose, correlation, timestamp and authenticated completion outcome. Anonymous events have no invented actor. Never record nonce/state, consent URL, invited email, tokens, client secret or raw Graph payload. Preserve existing redemption audit semantics.

## Registration as code

Add `infra/entra/delegated-permissions.json`, containing the distinct sorted scope names derived from `GraphScopeCatalog.CapabilityEvaluationScopes`, Graph's public application ID and delegated permission type `Scope`. It contains no environment tenant/client IDs, credentials or tokens. A unit test compares the manifest's exact names/type with the catalog and verifies the union of operation-specific scope groups is covered. CI fails on scope drift; catalog changes must update the reviewed manifest.

Add an idempotent `infra/scripts/configure-entra-onboarding.py` using the repository's existing Python/Azure CLI approach (`az rest` to Microsoft Graph). It updates existing registrations only:

1. Require explicit expected home-tenant ID, API app ID, customer SPA app ID, API application-ID URI and exact sign-in/consent redirect URIs. Verify the current Azure CLI tenant matches and both application objects are present in that tenant. Default to a sanitized dry-run diff; require `--apply` for writes.
2. Resolve Graph delegated scope GUIDs by matching enabled `oauth2PermissionScopes.value` on the Graph service principal, and resolve the API's enabled `access_as_user` scope ID. Fail before writing if any catalog scope is missing, disabled or ambiguous. Do not maintain a copied list of opaque Graph scope IDs.
3. Set API and customer SPA `signInAudience=AzureADMultipleOrgs`. Add only the customer SPA app ID to the API's `knownClientApplications`, preserving unrelated existing entries. The separate platform-admin SPA is not bundled and is not otherwise modified.
4. Generate the API Graph `requiredResourceAccess` `Scope` entries from the checked manifest and live scope-ID lookup. Replace its Graph block with the complete reviewed delegated set; fail on unexpected existing Graph application permissions instead of retaining them silently. Preserve non-Graph resource blocks.
5. Set the customer SPA's API resource block to delegated `access_as_user` only. Refuse unexpected direct Graph permissions or `platform.admin` on this customer client; require operator cleanup rather than extending customer consent. Preserve other resource blocks only when explicitly shown in the dry-run diff.
6. Add customer SPA `/auth/callback` as a SPA redirect and `/onboarding/consent/callback` as a Web redirect for the admin-consent handoff, which returns parameters rather than an auth code. Keep the API's existing Web consent callback for legacy re-consent. Merge explicit redirect URIs without deleting unrelated registrations.
7. Re-read and verify written audience, client linkage, resource access and redirects; fail visibly on partial writes. Rerunning converges. Never grant admin consent, create client secrets, modify customer tenants or assign roles.

Use the API's actual configured application-ID URI when constructing `{application-id-uri}/.default`; `api://{api-client-id}` is the default, not a browser-selected string. Add `Onboarding:CustomerClientId` and `Onboarding:ApiApplicationIdUri`, mapped from the existing environment client/audience values, through `.env.example`, Compose and hosted infra/workflow configuration. Customer `VITE_ENTRA_AUTHORITY` defaults/documentation use `organizations`; the onboarding action overrides it with the validated tenant authority. Keep platform-admin authority unchanged.

The one-consent screen must list both SPA → API and API → Graph requirements in the dedicated-tenant check. If it does not, block rollout and fix registration/linkage; do not silently fall back to two prompts or app-only permissions. Publisher verification remains an Atea registration-owner task outside this script.

## Documentation and rollout

Update directly related documentation during implementation:

- `README.md`: domain-or-GUID provisioning, one-link consent-first journey, delegated security boundary and configuration/script entry point.
- `docs/testing/test-tenant.md`: fresh second tenant with both customer service principals absent and user consent disabled; script dry-run/apply; invitation consent then nominated-account sign-in; replay/denial/wrong-account cases; scope verification and cleanup.
- `docs/operations/azure-deployment.md`: new onboarding configuration mapping, multi-tenant audience/linkage, exact customer SPA Web consent redirect alongside existing API redirect, and registration verification before deployment.
- `docs/security/entra-app-registration.md`: explain client IDs, API-resource `.default` versus Graph `.default`, complete manifest, known-client bundling, broad delegated scope review and retained re-consent. Update threat-model/data-isolation notes for the anonymous nonce boundary and redacted URLs.

Register permissions and callbacks before releasing the new journey. Deploy additive migration before application changes. Enable customer invitations only after deterministic checks and the opt-in fresh-tenant flow pass. Candidate-host setup cannot round-trip tab `sessionStorage` through a primary-host callback: consent-first acceptance runs on the configured primary origin, not a different candidate origin. Candidate sign-in smoke tests remain as documented; do not copy pending invitation state between origins.

Rollback keeps existing invitation URLs, GUID requests and legacy consent usable. Nullable/defaulted migration columns do not require a destructive rollback. A browser transaction in flight across rollback may require restarting from its original invitation.

## Testing and acceptance evidence

### API unit tests

- Tenant-domain normalization, fixed discovery URL, issuer/endpoint validation, redirects rejected, payload/time limits, failure mapping/cache, GUID bypass and mutually exclusive input. Use fake handlers/resolvers; no network.
- Preview eligibility and minimal DTOs; admin versus member invitation behavior.
- Legacy and invitation state formats, tamper/wrong purpose/workspace/tenant/invitation, malformed/oversized values, expiry and minimum invitation/challenge expiry.
- Scope manifest exact set/type and operation-group coverage; scope availability classification, concurrency/cancellation and no role-read dependency.
- Comprehensive health mapping, initial partial/unavailable transitions, later re-verification and no pre-redemption connected transition. Update only state-test expectations intentionally changed by these verification rules.

### API integration tests

- Existing authenticated GUID provisioning and platform/customer policies remain covered; domain provisioning with fake discovery resolves/deduplicates correctly and persists nothing on discovery failure.
- Anonymous preview/start/resume bypass fallback auth only on these routes; customer/workspace/platform routes still require their existing authentication and membership.
- Minimal anonymous output, malformed/expired/revoked/redeemed invitation, denial, Origin enforcement, rate limiting and safe configuration/database failure.
- SPA client ID, tenant GUID, API-resource `.default`, exact callback URI and signed state in generated URL; legacy API/Graph URL still works.
- PostgreSQL challenge persistence and atomic consume; two concurrent completion attempts; wrong-token tenant/identity, wrong invitation, forged callback tenant, reissue/revoke races and no Graph before authenticated completion.
- Redemption records the object ID; ordinary replay still fails; challenge-bound same-identity retry succeeds without duplicate membership/audit; another identity cannot resume redemption.
- Valid invitation completion runs automatic verification exactly once and returns partial/unavailable honestly. Legacy callback integration assertions remain unchanged. Check retries use health, not a second consent consume.

### Web unit tests

- Anonymous landing/callback render before `AuthProvider` sign-in guard and without session/capability/token calls.
- Setup copy, expandable exact permissions, accessibility/status/error states and member-invitation fallback.
- Start saves tab state before navigation; stale/missing/mismatched state and denied callback do not trigger sign-in or redemption.
- Tenant-pinned MSAL authority, selected account, redirect return path and existing invitation MSAL-state behavior.
- Successful sign-in → one redemption → one completion → health result → overview; lost-response recovery, StrictMode rerenders and double clicks do not duplicate requests. Deduplicate in-flight requests and persist the pending step rather than relying only on component-local refs.
- Existing invitation redemption, onboarding page, legacy consent callback and auth tests continue to exercise retained paths.

### Tooling and real-tenant evidence

Test the registration script with fake Azure CLI/Graph responses: tenant mismatch, disabled/missing scopes, unexpected app permissions, dry run, idempotent patch, preserved settings and partial-write verification. No live tenant/registration writes in CI.

Run focused existing .NET unit/integration selectors and Vitest tests for the changed boundaries, then the existing API/Web builds and relevant infra contract tests. Container-backed tests require the existing Docker setup; record environmental skips rather than replace PostgreSQL semantics with a permissive fake.

Manual acceptance uses disposable registrations and a dedicated external organizational tenant:

1. Remove only approved test service principals and disable user consent in that test tenant.
2. Provision by domain, open invitation logged out, and verify there is no pre-consent API sign-in.
3. Use an active delegated-consent-capable administrator. Confirm one consent screen provisions both customer service principals and required delegated grants without manual customer registration.
4. Sign in as the nominated user, verify one redemption and automatic baseline/scope results, then reach overview.
5. Repeat denial, non-admin, wrong-tenant/account, invitation expiry/reissue, missing configured scope and revoked-consent cases. Verify legacy post-sign-in re-consent.
6. Capture only safe statuses, timestamp, scope names and correlation/object identifiers. Never capture invitation URL, state, browser storage, tokens or raw provider/Graph payloads.

Deterministic tests prove orchestration and boundaries; the live test proves registration behavior. Neither alone is a production security approval.

## Out of scope

- Self-service signup without an Atea invitation, tenant auto-admission or anonymous workspace creation.
- App-only Graph permissions/client-credentials operations, automatic directory-role assignment or PIM activation.
- GDAP/Partner Center relationships and customer-managed application registrations.
- Publisher verification automation. Atea should complete it as a non-code registration-owner action before external customer rollout.
- Email delivery, background verification jobs, multi-cloud authorities, configurable per-customer permission packages, or unrelated workspace/auth refactoring.

## References

- Research: “Simplifying customer onboarding with multi-tenant admin consent”, 2026-10-07, supplied research artifact.
- [Microsoft identity platform admin consent protocol](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent): tenant-specific consent and callback tenant warning.
- [Convert an application to multitenant](https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant): multi-tier consent and `knownClientApplications`.
- [Grant tenant-wide admin consent](https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/grant-admin-consent): role requirements and tenant policy.
- [Application manifest reference](https://learn.microsoft.com/en-us/entra/identity-platform/reference-app-manifest): audience, required resource access and known clients.

## Spec self-review

Reviewed for placeholders, scope, ambiguity and internal contradictions. The design explicitly separates anonymous callback resolution from authenticated single-use completion; binds recovery to the actual redeemer; keeps callback tenant non-authoritative; specifies the API-resource `.default` for SPA-targeted bundled consent; preserves legacy callback behavior; and distinguishes baseline connectivity, permission coverage and role authority. No product code or live-registration changes are part of this design commit.
