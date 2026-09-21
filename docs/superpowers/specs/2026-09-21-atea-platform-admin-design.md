# Atea platform-admin console design

## Status

Approved in conversation on 2026-09-21. This specification covers the first implementation of the Atea operator console and its development-only local authentication. Atea Entra federation is explicitly deferred.

## Goal and boundary

The application needs an Atea-only administration surface for provisioning and operating customer workspaces while the customer-facing administration experience continues to use the customer tenant's Entra identity and effective Microsoft Graph permissions.

The first release of this surface will allow an authorized Atea administrator to:

- create a workspace for a verified customer Entra tenant;
- view workspace metadata and onboarding/connection state;
- add customer administrators and authorized Atea operators by Entra object ID;
- create and display a one-time onboarding invitation instruction.

It will not grant Microsoft Graph permissions, manage customer directory objects, or provide a hidden Atea super-admin path into a customer tenant. Customer directory actions remain delegated and role-aware through the existing customer Entra flow.

## Authentication and authorization

The product will expose an authentication-provider boundary for the Atea console:

- `LocalDevelopment` is enabled only when the API environment is `Development` and an explicit local-admin configuration gate is enabled.
- The local provider accepts credentials configured through ignored local environment settings, validates them server-side, and issues an encrypted, `HttpOnly` session cookie. Credentials and session values are never placed in browser local storage or logged.
- Startup will reject local-admin configuration when the environment is not `Development`.
- The local administrator receives platform-admin claims only. The existing `IPlatformAuthorization` boundary remains authoritative for `/api/platform/*` routes.
- Local development may use an explicit `AllowAllWorkspaces` scope gate so the configured local administrator can complete a full demo onboarding flow after creating a workspace. This gate is accepted only in `Development`; production and future Atea Entra federation require explicit workspace scopes.
- Customer routes continue to require Entra bearer authentication and workspace membership. A local Atea admin session cannot be used to call customer Graph operations.
- The future `AteaEntra` provider will replace the local provider behind the same boundary and map Atea Entra users/groups to platform authorization without changing the admin UI contract.

The local identity must have a stable configured object identifier so the existing platform workspace-scope authorization can be reused. Workspace-specific scope remains enforced for membership and invitation operations.

## API surface

Existing platform endpoints remain the source of truth for mutations. The implementation will add the minimum read endpoints needed by the UI:

- `GET /api/platform/workspaces`
- `GET /api/platform/workspaces/{workspaceId}`
- `POST /api/platform/workspaces`
- `POST /api/platform/workspaces/{workspaceId}/memberships`
- `POST /api/platform/workspaces/{workspaceId}/invitations`

Local authentication will provide login, logout, and current-session operations under a dedicated admin-auth namespace. All platform endpoints will preserve the existing server-side authorization checks, workspace scoping, duplicate prevention, and safe response rules. Invitation instructions are returned once to the authorized operator and are not emailed, persisted as usable plaintext, or written to logs.

## Frontend experience

The React application will add a distinct Atea admin route tree and shell rather than exposing platform operations through customer navigation:

- a local admin sign-in page;
- a workspace list with tenant ID, display name, connection state, and freshness/status information;
- a create-workspace form with verified tenant ID and display name;
- a workspace detail view with memberships, invitation creation, and safe onboarding output;
- loading, empty, validation, authorization, duplicate, and expired-instruction states.

The console will reuse the existing Atea logo, Inter font assets, semantic theme tokens, focus treatment, and light/dark mode. Neutral surfaces and readable grey text will carry most of the interface; Atea green will identify primary actions. The admin area will remain usable at narrow widths and with keyboard navigation.

## Data flow

1. A local Atea administrator signs in through the development-only provider.
2. The UI loads the platform workspace list through the authenticated API.
3. Workspace creation records the verified tenant ID and starts the existing onboarding state machine.
4. The operator adds a nominated customer administrator or Atea guest using the customer's Entra object ID.
5. The operator creates an invitation instruction and hands it to the customer through the agreed channel.
6. The customer redeems the invitation, receives workspace membership, grants delegated Graph consent, and then uses the existing customer-facing application flow.

The browser never selects an active customer tenant for directory operations. The API resolves customer context from verified identity, tenant, and workspace membership as it does today.

## Testing and rollout gates

Tests will cover:

- local login success, failure, logout, cookie behavior, and disabled-environment rejection;
- platform-admin authorization and workspace-scope enforcement;
- workspace creation and listing;
- membership and invitation creation with duplicate and cross-workspace rejection;
- frontend admin route guards, form validation, empty/loading/error states, and invitation handoff;
- regression that customer Entra authentication and Graph capability evaluation remain unchanged.

Local verification will use the existing PostgreSQL Docker service and the dedicated demo tenant. Azure deployment will not enable local authentication. A later federation task will add the Atea Entra provider and production configuration validation.
