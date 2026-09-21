# Atea Unified Workplace Local V1 Completion Design

**Date:** 2026-09-21  
**Status:** Approved implementation direction from the existing product design and user request  
**Scope:** Complete the local, real-test-tenant V1. Azure deployment is explicitly deferred.

## Outcome

The local product must support one complete journey:

1. A local Development-only Atea platform administrator signs in at `/admin`.
2. The administrator creates a workspace for a verified Microsoft Entra tenant, adds the nominated customer administrator, and creates an invitation.
3. The customer administrator opens a working `/invitations/{nonce}` URL, signs in with Entra ID, redeems the invitation, and is taken into the customer workspace.
4. The customer administrator grants or confirms delegated Graph consent, runs a connection check, and reaches a truthful connection state.
5. The customer administrator uses the permission-aware Overview, Users, Licenses, Audit and Workspace Settings areas against the real test tenant.
6. User lifecycle, group membership, license assignment/removal and directory-role PIM actions either execute through delegated Graph or present a precise guided handoff when the tenant requires approval, MFA, consent or another missing permission.

## Existing baseline and completion gaps

The current branch already contains the PostgreSQL workspace model, local Atea admin console, delegated Graph adapters, capability evaluation, user/license/group/PIM API services, most customer screens, audit/settings screens, and automated unit/integration/browser-style tests. V1 completion therefore extends and wires the existing boundaries rather than introducing a second architecture.

The known gaps are:

- `Onboarding:PublicBaseUrl` defaults to `https://workplace.example` and is not present in the local environment example.
- The invitation URL has no customer-facing frontend route, even though the API redemption endpoint exists.
- The consent start link has no complete browser callback/refresh journey.
- The user create/edit/group/license components are present but not consistently connected to visible actions and refreshes.
- Tenant-role and license user filters are rendered as disabled placeholders.
- Real-tenant local verification does not yet prove the complete Atea-to-customer handoff.

## Architecture

The SPA keeps two deliberate route trees:

- `/admin` uses the Development-only local Atea cookie provider and platform-scoped workspace endpoints.
- Customer routes, including `/invitations/{nonce}`, use the existing Entra MSAL provider and bearer API client. Customer APIs continue to resolve tenant and workspace from verified claims and membership.

The API remains the only Graph caller. Invitation redemption occurs only after Entra authentication and validates tenant, invited identity, expiry and one-time nonce state. Consent is an explicit tenant-admin action; the app records only safe state and correlation metadata. Azure deployment is not part of this design, but all local configuration names remain compatible with the existing Azure plan.

## Local V1 contract

The local `.env` must provide:

```text
Onboarding__PublicBaseUrl=http://localhost:5173
Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
Onboarding__ConsentSigningKey=<base64-encoded-32-byte-development-key>
```

The API must reject an unsafe or missing public base URL in Development rather than silently generating a placeholder link. The public URL may be HTTP only for local Development and must be HTTPS for future hosted environments.

The invitation redemption response contains only safe workspace status and next-step metadata. It never returns a nonce, access token, refresh token, client secret, or Graph response.

## V1 completion rules

- Every visible customer action has a working API call, a pending state, a success state, a translated failure state, and a refresh path.
- Capability states are enforced by the API and reflected in the UI as hidden, read-only, disabled-with-reason, consent-required, PIM-required, approval-required or unavailable.
- Mutation requests carry idempotency keys and require explicit confirmation where the existing API contract requires it.
- Directory data remains Graph-authoritative; platform PostgreSQL stores metadata, connection state, invitations, settings, idempotency records and safe audit events only.
- Local demonstration may use the real M365 E5 test tenant. Graph fixture tests remain mandatory for deterministic CI-like verification.
- The final local runbook must prove both route trees, invitation redemption, consent/connection state, customer RBAC differences and PIM fallback behavior.

## Acceptance evidence

The V1 is complete when the following are all demonstrated:

- A generated local invitation opens a real frontend page instead of `workplace.example` or a missing route.
- A wrong-tenant, wrong-user, expired or reused invitation cannot create membership.
- A redeemed customer can reach `/overview` and the API reports a truthful connection state.
- A Global Reader can read allowed data while mutations are hidden or blocked; a User Administrator can perform the supported user actions; an eligible inactive PIM role presents activation or guided handoff.
- User creation, profile edit, disable/reactivate, group add/remove and license assign/remove are usable from the UI and protected by API capability checks.
- The full local test commands, API tests, web tests, E2E tests, build and Docker Compose validation pass, with any real-tenant checks explicitly opt-in.
