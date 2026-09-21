# Atea Unified Workplace threat model

Status: MVP design and test-boundary model, 2026-09-21. This document describes the intended security properties and the evidence currently available from deterministic tests. It is not proof that a real Entra tenant is configured correctly.

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

1. MSAL signs the user into the workforce SPA and requests the API scope (`access_as_user`). Redirect state is kept in browser `sessionStorage` for the tab session.
2. The browser calls same-origin `/api/...` routes with the API bearer token. The API authenticates the token, uses verified tenant/object claims to select the workspace, and evaluates workspace membership and capability state.
3. For a permitted directory operation, the API uses the approved delegated Graph client/permission set server-side. Graph tokens, refresh tokens, client secrets, `Authorization` headers and raw Graph responses are not persisted, returned to the browser, or written to audit metadata.
4. The API returns safe application data, capability decisions, stable error states and correlation references. Graph request IDs may be returned as safe correlation metadata; they are not credentials.

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

Customer administrator consent is an onboarding prerequisite, not workspace authorization. The API records verified workspace membership separately and exposes connection states such as `awaiting_invitation`, `consent_required`, `connected`, `permission_incomplete`, `temporarily_unavailable` and `consent_revoked`.

Consent start is an API-mediated handoff; the browser receives an authorization descriptor, not a Graph token. After revocation, connection checks and later Graph operations must fail closed with `consent_revoked`/permission guidance, and cached capability state must not authorize mutations. Re-consent must be an explicit customer-admin action. Token caches and browser sessions are not evidence that consent remains valid.

## Browser-boundary evidence and assumptions

`tests/Web.E2E/security-boundaries.spec.ts` uses the existing real components and deterministic injected loaders/fetch fixtures to verify that:

- requests stay on the current origin and use `/api/...` routes;
- request URLs, headers, bodies and rendered DOM contain neither `graph.microsoft.com` nor a Graph access-token sentinel;
- consent-required and read-only capability states render the current denied/permission UX without contacting a tenant or attempting a mutation.

Known assumptions and limitations:

- The repository's Web.E2E package is currently Vitest/jsdom despite its name; this is not a live Playwright/browser or real-tenant proof.
- The new `.ts` spec is intentionally kept within the requested write scope. The existing Vitest config has an explicit `.tsx` include list, so focused execution requires a disposable config override until that config is intentionally updated in a separate task.
- Fixtures use synthetic workspace, actor and API-token values. No tenant IDs, credentials, Graph tokens or B2B accounts are used.
- These tests cannot prove API-side claim validation, Graph scope correctness, PIM policy enforcement, revocation propagation, audit persistence, cross-tenant isolation or CSP/XSS resilience. Those properties require the API/integration and opt-in real-tenant scenarios.
- Real-tenant checks must remain opt-in, use disposable accounts and short-lived credentials from a local secret store, avoid CI logs, and capture object IDs/role assignments rather than secrets.

## Required follow-up evidence

Before production readiness, retain evidence for cross-tenant and cross-workspace denial, mutation authorization and replay protection, consent/revocation, PIM activation/approval/MFA, B2B membership removal, throttling/retry behavior, and persisted audit redaction/retention. The evidence package should include commands, timestamps, correlation IDs and safe outcome summaries only.
