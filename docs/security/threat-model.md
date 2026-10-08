# Atea Unified Workplace threat model

Status: Updated 2026-10-08. This document describes the implemented security
boundaries and the evidence currently available from deterministic tests. It is
not proof that a real Entra tenant is configured correctly.

## Assets and trust boundaries

Protect:

- customer tenant identity, workspace membership and actor object IDs;
- delegated Microsoft Graph authority and PIM state;
- directory data, mutation intent and mutation results;
- consent state, revocation state and audit/correlation metadata.

The relevant boundaries are:

1. **Browser to API.** The React app is untrusted presentation code. It may hold the workforce API access token needed to call the platform, but it must never receive a Microsoft Graph access token, Graph payloads that contain secrets, or a Graph endpoint URL.
2. **API to Entra/Graph/PIM.** The API validates the caller and performs delegated Graph work server-side using operation-specific permissions. Graph and PIM remain authoritative for directory state and final mutation results.
3. **API to persistence.** Every authenticated request is resolved to a stored workspace from verified `tid` and `oid` claims. Persistence operations require the resolved `WorkspaceId`; client-supplied tenant/workspace selectors are not authorization inputs.
4. **Atea operator to customer workspace.** Atea platform administration is an allowlisted platform role. It may provision or manage workspace metadata, but it does not grant Microsoft 365 directory authority.

## Token and data flow

1. An eligible invitation can be previewed anonymously using its bearer nonce. The response is minimal: workspace display name, flow and public delegated scope names. It does not return tenant/workspace IDs, invitee identity, membership details, module assignments or connection diagnostics.
2. Before sign-in, the customer administrator can start a tenant-specific Entra admin-consent handoff. The API—not callback query parameters or browser input—selects the invitation tenant, customer SPA client ID, configured API application-ID URI and exact callback. A signed, expiring, invitation-bound state is persisted by hash. The browser keeps the pending transaction in that tab's `sessionStorage`; this state is not an identity or authorization proof.
3. After consent returns to the same origin/tab, the API validates the pending state and returns the trusted tenant for sign-in. MSAL requests the API scope (`access_as_user`) for that tenant. The authenticated API then checks verified token `tid`/`oid`, invitation eligibility and one-time redemption before creating membership or completing consent.
4. The API verifies delegated Graph access server-side and reports connection and permission coverage separately. A callback's `tenant`, `admin_consent` or raw provider error cannot prove identity or a grant. `consent_received` means callback processing only, not a connected workspace.
5. For a permitted directory operation, the API uses the approved delegated Graph client/permission set server-side. Graph tokens, refresh tokens, client secrets, `Authorization` headers and raw Graph responses are not persisted, returned to the browser, or written to audit metadata.
6. The API returns safe application data, capability decisions, stable error states and correlation references. Graph request IDs may be returned as safe correlation metadata; they are not credentials.

Primary threats are browser-side token exfiltration, accidental direct Graph calls, over-broad delegated scopes, and confused-deputy use of a customer token. Controls are same-origin API calls, server-side Graph adapters, least-privilege operation scopes, verified claims, workspace membership checks, and fail-closed capability states.

## Tenant and workspace isolation

- A client cannot switch tenants by changing a URL, request body, header or workspace ID. The server joins verified `tid`/`oid` claims to the stored membership and resolves the active workspace.
- Repositories and audit queries are workspace-scoped; tenant and membership keys are unique in persistence. Cross-workspace reads, mutations and audit reads must be rejected even when an attacker knows another ID.
- Data returned to the UI is limited to the active workspace and to the caller's capability snapshot. The UI is not an isolation boundary; API and Graph checks repeat the authorization decision.
- Platform storage is metadata-only: no access/refresh tokens, client secrets, passwords, raw Graph payloads or full upstream exceptions.

Residual risk: isolation depends on every endpoint and repository path using the same verified workspace resolver. The security test suite must keep cross-tenant, cross-workspace and replay cases as API-level tests; the browser tests below cannot prove server isolation.

## Entra RBAC and PIM

Capability evaluation keys on stable Entra role template IDs, not display names. Delegated scopes and active tenant-wide role assignments are both required for directory mutations. Global Reader is read-only; User Administrator, Groups Administrator, License Administrator and Privileged Role Administrator receive only their corresponding capabilities when the required scopes and policy conditions are present.

Eligible-but-inactive roles map to `pim_activation_required`. Approval, MFA and expired eligibility map to explicit PIM states. Activation is never silent: the UI presents confirmation or a guided Microsoft Entra handoff, and the API/Graph/PIM result remains authoritative. Administrative-unit-scoped assignments do not satisfy tenant-wide mutation requirements.

The browser permission UX is deliberately non-authoritative:

- `hidden` renders a generic permission-required state and does not load the protected route;
- `read_only` renders the reason and disables mutation controls;
- `consent_required` explains that delegated Graph consent is required;
- unavailable or failed authorization maps to retryable, fail-closed messaging.

## B2B and Atea operator access

Customer-assigned Atea B2B guests are treated as customer identities. Guest access requires active workspace membership, the customer-assigned role/capability, valid consent and a current session; removing membership or role must deny subsequent access and mutations.

Atea operators are identified by an allowlisted Entra object ID and receive platform workspace-administration capabilities only. B2B status, operator status and Microsoft 365 directory roles are separate decisions. No Atea operator path may infer customer Graph authority from platform administration alone.

## Audit and incident evidence

The platform audit stream records workspace-scoped actor intent, action, target, outcome, safe failure category and correlation references. Microsoft 365 Purview audit logs remain authoritative for directory changes. Store Graph request IDs, platform correlation IDs and PIM request IDs only as references; never store tokens, authorization headers, passwords, raw Graph payloads or full exception messages.

Incident review must identify the workspace and tenant first, then correlate platform events with Graph/PIM request IDs and the Microsoft 365 audit record. Audit reads are themselves workspace-scoped and capability-protected.

## Consent and revocation

Customer administrator consent is an onboarding prerequisite, not workspace
authorization. The anonymous boundary is limited to invitation preview,
eligible consent start and state resume; it does not redeem invitations,
create memberships or mutate connection status. Preview/start use the same
generic unavailable response for malformed, expired, revoked and redeemed
nonces. Same-origin POSTs, trusted-proxy-aware rate limits, bounded signed
state, one-time hash-backed consumption and invitation/redeemer binding limit
replay and cross-origin abuse. A challenge lasts at most 20 minutes and no
longer than its invitation. Do not log invitation URLs, nonce/state, browser
storage, provider descriptions or raw Graph responses.

The consent-first request targets the customer SPA and uses the API resource's
configured `/.default` scope; the retained authenticated member re-consent
targets the API and uses Graph `/.default`. The customer SPA has no direct
Graph or `platform.admin` permissions, and the platform-admin SPA is not
bundled. Both registrations must remain delegated-only for this workflow.

The API records verified workspace membership separately and exposes
connection states such as `awaiting_invitation`, `consent_required`,
`connected`, `permission_incomplete`, `temporarily_unavailable` and
`consent_revoked`. Consent itself does not assign Entra roles, activate PIM or
prove that all configured Graph scopes are available.

Consent start is an API-mediated handoff; the browser receives an authorization descriptor, not a Graph token. After revocation, connection checks and later Graph operations must fail closed with `consent_revoked`/permission guidance, and cached capability state must not authorize mutations. Re-consent must be an explicit customer-admin action. Token caches and browser sessions are not evidence that consent remains valid.

## Browser-boundary evidence and assumptions

`tests/Web.E2E/security-boundaries.spec.ts` uses the existing real components and deterministic injected loaders/fetch fixtures to verify that:

- requests stay on the current origin and use `/api/...` routes;
- request URLs, headers, bodies and rendered DOM contain neither `graph.microsoft.com` nor a Graph access-token sentinel;
- consent-required and read-only capability states render the current denied/permission UX without contacting a tenant or attempting a mutation.

Known assumptions and limitations:

- The repository's Web.E2E package is currently Vitest/jsdom despite its name; this is not a live Playwright/browser or real-tenant proof.
- Fixtures use synthetic workspace, actor and API-token values. No tenant IDs, credentials, Graph tokens or B2B accounts are used.
- Deterministic API/web tests and fake Azure CLI/Graph responses cannot prove a real tenant's `knownClientApplications` consent bundling, live delegated grants, PIM policy enforcement, revocation propagation, audit persistence or CSP/XSS resilience. Those properties require the API/integration and opt-in fresh-tenant scenarios.
- Real-tenant checks must remain opt-in, use disposable accounts and short-lived credentials from a local secret store, avoid CI logs, and capture object IDs/role assignments rather than secrets.

## Required follow-up evidence

Before production readiness, retain evidence for cross-tenant and cross-workspace denial, mutation authorization and replay protection, consent/revocation, PIM activation/approval/MFA, B2B membership removal, throttling/retry behavior, and persisted audit redaction/retention. The evidence package should include commands, timestamps, correlation IDs and safe outcome summaries only.
