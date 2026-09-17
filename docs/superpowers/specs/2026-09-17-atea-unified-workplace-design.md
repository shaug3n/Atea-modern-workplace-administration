# Atea Unified Digital Workplace Platform

## Design specification

**Date:** 2026-09-17  
**Status:** Approved for implementation planning  
**Working title:** Atea Unified Digital Workplace Platform

## 1. Product intent

Atea will deliver a hosted administration service that gives each customer a unified, permission-aware workspace for managing Microsoft 365 users and licenses. The platform is not an off-the-shelf tenant-independent admin console. It is an Atea-operated service with one shared product foundation, isolated customer workspaces, customer-specific configuration, and optional Atea operational access.

Customers manage their own tenant when they choose to. If Atea operates the tenant, Atea staff access the same customer workspace as B2B guests in that customer tenant, using only the roles and permissions that the customer has granted them.

The first release focuses on tenant onboarding and user administration. Devices, advanced statistics, and broader operational modules are designed as later extensions.

## 2. Goals

The first release must:

1. Allow Atea to provision a customer workspace and invite a nominated customer administrator.
2. Guide an authorized customer administrator through Microsoft 365 tenant connection and delegated consent.
3. Allow an authorized Atea B2B guest to complete the connection when the customer has already granted the necessary access.
4. Provide a searchable, filterable user directory backed by Microsoft Graph.
5. Show user details, licenses, group memberships, tenant roles, and PIM state.
6. Support user lifecycle operations: create, edit the defined profile fields, disable/reactivate, group membership changes, and license assignment/removal.
7. Adapt the UI and API to the signed-in user’s effective Entra permissions.
8. Provide explicit PIM states and in-app activation when the tenant policy and permissions allow it.
9. Record platform actions, permission denials, consent changes, and PIM requests in an auditable event stream.
10. Run locally for development and demonstrations, then deploy the same application to Azure for external customer access.
11. Use Atea’s visual identity, with a light theme as the default and a user-controlled dark mode.

## 3. Non-goals for the first release

The first release will not include:

- Device inventory, Intune compliance, or warranty management
- A full executive statistics and reporting suite
- Background application-permission synchronization of directory records
- Password reset, MFA reset, session revocation, or authentication-method management
- Bulk user administration
- Customer self-service workspace signup
- Separate customer-specific codebases or deployments
- A second application RBAC system for Microsoft 365 operations
- PIM for Azure resource roles or PIM for groups. v1 covers Microsoft Entra directory-role PIM only.

## 4. Operating model

### 4.1 Shared hosted service

The service is operated by Atea as a shared Azure-hosted platform. Each customer has one isolated workspace identified by a verified Microsoft Entra tenant identifier. The product uses a single versioned codebase.

Customer-specific behavior is configuration-driven and limited to approved settings such as:

- Enabled modules
- Navigation visibility
- Default columns and filters
- Workspace display name and secondary customer mark
- Support instructions
- Tenant-level default for light or dark display mode

Customer configuration cannot inject CSS, alter authorization semantics, or bypass Atea identity rules. The Atea logo remains the primary product identity.

### 4.2 Access actors

There are three relevant actor types:

- Customer members: users who authenticate from the customer’s own Entra tenant.
- Atea operators: Atea users represented as B2B guests in the customer tenant with customer-assigned Entra roles.
- Platform workspace administrators: narrowly scoped users who may complete onboarding and change platform workspace metadata. This platform role never grants Microsoft 365 directory rights.

The product must not contain a hidden Atea super-admin path for customer directory operations. Atea operational access exists only when the customer tenant has created the B2B guest relationship and assigned the required role.

## 5. Authorization model

### 5.1 Authentication

All users authenticate with Microsoft Entra ID. The application is registered as a multitenant workforce web application so customer users and B2B guests can sign in with work or school accounts.

The backend validates:

- Token issuer and audience
- Tenant context
- User object identity
- Customer workspace membership
- B2B guest relationship where applicable
- Session integrity and expiry

The client cannot choose an arbitrary customer tenant by passing a tenant identifier. The active workspace is resolved from verified server-side identity and workspace membership.

### 5.2 Delegated Microsoft Graph access

Microsoft 365 operations use delegated Graph access. The backend calls Graph on behalf of the signed-in user rather than using a tenant-wide service identity for v1 directory mutations.

The application requests only the Graph delegated permissions required by the v1 operation set. The final permission matrix must be documented from Microsoft Graph’s least-privilege permission reference during implementation.

The effective authorization result is the intersection of:

1. The Graph delegated scopes consented for the customer tenant.
2. The signed-in user’s effective Entra roles, custom permissions, group-based assignments, administrative-unit scope, PIM state, and tenant policies.
3. The platform workspace permission for platform-only operations.

The platform must not treat a role label as a universal guarantee. The Graph request remains authoritative for the concrete resource and operation.

### 5.3 Capability model

The backend exposes a capability set with states rather than a single administrator flag. Initial capabilities include:

- `workspace.view`
- `workspace.configure`
- `users.view`
- `users.create`
- `users.edit`
- `users.disable`
- `users.groups.manage`
- `users.licenses.manage`
- `users.audit.view`
- `pim.view`
- `pim.activate`

Capability states include:

- `allowed`
- `read_only`
- `hidden`
- `pim_activation_required`
- `approval_required`
- `mfa_required`
- `consent_required`
- `not_eligible`
- `not_authorized`
- `temporarily_unavailable`

The UI uses these states to hide unavailable sections, show read-only information, disable actions with a clear explanation, or offer a PIM activation path. The API enforces the same capability before every operation. UI hiding or disabling is never the security boundary.

### 5.4 PIM behavior

The first PIM implementation covers Microsoft Entra directory roles. For a user with an eligible but inactive role, the platform shows:

- The role required
- Why the current action needs it
- Whether activation can be initiated in the platform
- Whether MFA, justification, approval, or a time limit is required
- The current request or activation state

When supported by the tenant’s Graph permissions and PIM policy, the user can explicitly request activation. The platform must not silently activate roles. If Graph cannot complete activation because of approval, MFA, policy, or missing permissions, the platform presents a guided handoff with the exact next step and preserves the current state.

PIM responses must be mapped to user-facing states rather than surfaced as raw Graph errors.

## 6. Onboarding and tenant connection

### 6.1 Atea-provisioned flow

1. An Atea operator creates a workspace and records the customer’s verified Entra tenant identifier.
2. The workspace is created in `awaiting_invitation` state.
3. Atea nominates a customer administrator and sends an invitation.
4. The nominated administrator signs in with Entra ID.
5. The platform confirms the signed-in tenant matches the workspace tenant.
6. The platform requests the minimum delegated Graph consent required by v1.
7. The platform verifies the connection by reading tenant and user capability information.
8. The workspace becomes `connected`.

### 6.2 Atea-led connection flow

An Atea operator can complete the customer connection when:

- The operator is represented as a B2B guest in the customer tenant.
- The operator has sufficient active Entra permissions for the consent and Graph operations.
- The customer workspace has explicitly authorized that operator for onboarding.

If any requirement is missing, the platform does not elevate or bypass access. It explains the missing relationship, consent, role, or PIM activation and gives the operator a customer-admin handoff path.

### 6.3 Connection states

The connection state machine is:

- `awaiting_invitation`
- `consent_required`
- `connected`
- `permission_incomplete`
- `temporarily_unavailable`
- `consent_revoked`
- `connection_failed`

The state is visible in the workspace overview and onboarding screen. The platform records the actor, tenant, granted scope set, timestamps, and correlation identifiers without storing access tokens.

## 7. User administration experience

### 7.1 Workspace shell

The default interface is a light Atea operations workspace with:

- Atea logo in the global shell
- Tenant and workspace context in the header
- User-controlled light/dark display toggle
- Persistent navigation
- Green primary actions
- Neutral surfaces and dividers for data density
- Teal links and purposeful semantic status colours

The first navigation set is:

- Overview
- Users
- Licenses
- Audit activity
- Workspace settings, when permitted

The interface is English only in v1. All UI text is externalized so additional languages can be added later.

### 7.2 Workspace overview

The overview is an operational health page, not the full future dashboard. It shows:

- Tenant connection status
- Last successful delegated Graph request
- Consent and permission health
- PIM issues requiring attention
- Total users
- License coverage
- Recent platform activity

The page must distinguish live data, cached data, unavailable data, and stale data.

### 7.3 Users directory

The directory supports:

- Search by name, email, UPN, department, location, and job title
- Filters for account status, tenant role, license, and user type
- Pagination backed by Graph
- Freshness indicator and user-triggered refresh
- Permission-aware columns and actions
- Empty, loading, error, and no-access states

The directory must not render sensitive fields when the user lacks the required read capability. It must not rely on a preloaded full-tenant cache for authorization.

### 7.4 User details

The detail page contains:

- Identity and contact information
- Job and organization information
- Account status
- Assigned licenses
- Group memberships
- Tenant role assignments and PIM state
- Relevant platform audit activity

Each section is independently capability-aware. A user may see profile information while receiving a read-only explanation for groups, licenses, or role operations.

### 7.5 User lifecycle actions

The v1 action set is:

- Create user
- Edit the defined profile fields
- Disable user
- Reactivate user
- Add or remove group membership
- Assign or remove licenses

The approved editable profile fields are display name, given name, surname, job title, department, office location, mobile phone, usage location, and account-enabled state where the tenant permits the change.

The create and edit flows use explicit review and confirmation. The confirmation view shows:

- Target user
- Proposed change
- Required capability
- Any detected source-of-authority or tenant policy limitation
- Audit notice

Directory-synchronized or otherwise externally managed records may be read-only. The platform explains the source-of-authority limitation rather than presenting an action that is expected to fail.

User creation requires the approved identity fields plus the tenant-required sign-in attributes. When a temporary password is required, the backend generates it for the single Graph request, marks the account to change it at first sign-in, and displays it once to the initiating administrator after successful creation. The password is never logged, persisted, or returned again. If the tenant policy blocks the operation, the platform reports the reason and does not retain partial credentials.

## 8. Component boundaries

### 8.1 React frontend

The frontend owns:

- Shell, navigation, and display-mode preference
- Route-level data loading
- Capability-aware rendering
- Tables, filters, forms, confirmations, and status states
- Accessible error and permission messaging

The frontend does not call Microsoft Graph directly and does not hold Graph access tokens.

### 8.2 ASP.NET Core BFF/API

The backend owns:

- Entra authentication and secure session handling
- Workspace resolution and tenant isolation
- Delegated token acquisition
- Capability evaluation
- User, group, license, and PIM endpoints
- Audit recording
- Workspace configuration
- Graph error translation and throttling policy

### 8.3 Microsoft Graph adapter

The adapter is separated into focused interfaces:

- Directory reader
- User lifecycle command service
- Group membership service
- License service
- Entra role and PIM service
- Consent and connection health service

The adapter maps Graph response models, correlation IDs, throttling, authorization failures, and policy challenges into application-level results.

### 8.4 Platform data store

The platform database stores:

- Workspaces
- Verified tenant identifiers
- Connection and consent state
- Workspace configuration
- Platform membership and onboarding metadata
- Audit events
- User display preferences

Microsoft Graph remains the source of truth for users, groups, licenses, roles, and PIM state. v1 does not broadly replicate directory records into a shared cache.

## 9. Data isolation and audit

Every customer-owned platform record has an immutable workspace identifier. All repository methods require a server-created workspace context. The API rejects requests where the authenticated tenant, workspace membership, and route resource do not agree.

The audit event model records:

- Event ID
- Customer workspace and tenant
- Actor identity and customer-tenant object identity where available
- Action name
- Target resource identifier
- Result: requested, succeeded, denied, failed, or cancelled
- Timestamp
- Graph correlation/request ID
- PIM request ID where applicable
- Safe failure category

Tokens, client secrets, passwords, MFA data, and raw authorization headers are never stored or logged. Before-and-after values are limited to approved non-secret fields.

Microsoft 365 audit logs remain the authoritative record for directory changes. The platform audit log records the platform actor, intent, result, and correlation information.

## 10. Error and resilience behavior

The platform translates infrastructure and Graph failures into actionable states:

- `401`: refresh the session or request sign-in again.
- `403`: show the missing role, consent, PIM state, or policy explanation.
- `404`: explain that the object may have been removed or is no longer visible, then offer refresh.
- `409`: explain the conflicting state and avoid repeating the mutation automatically.
- `429`: respect Graph retry guidance and show a safe retry state.
- `5xx` or network failure: show temporary unavailability and allow a bounded retry.
- Consent or Conditional Access challenge: route the user through the required tenant-admin or MFA step.

Mutating operations use idempotency protection so a repeated browser submission cannot create duplicate effects. Audit events distinguish an attempted action from a confirmed Graph mutation.

Loading states use skeletons or progress indicators. Empty states explain whether there is no data, no matching data, or no permission to view data.

## 11. Local-first and Azure deployment

### 11.1 Local profile

The local profile runs:

- React frontend
- ASP.NET Core BFF/API
- PostgreSQL container
- Local configuration and development secrets
- Development Entra app registration

Local development uses a real Microsoft 365 test tenant for delegated Graph, RBAC, B2B guest, consent, and PIM validation. A mock Graph adapter may be used for isolated UI work, but it cannot replace real-tenant integration tests.

The local database uses PostgreSQL rather than SQLite to keep schema behavior aligned with production.

### 11.2 Azure profile

The Azure profile runs:

- Containerized React frontend and ASP.NET Core BFF/API
- Azure Database for PostgreSQL
- Azure Key Vault
- Application Insights and Log Analytics
- Azure Container Registry
- Managed certificates and controlled ingress
- Optional Azure Front Door for production edge protection and routing

The Azure environment is deployed to one Atea-approved EU/EEA region for v1. Resilience and a secondary region are later operational decisions, not a separate application model.

The same container images, migrations, configuration contracts, and Graph adapter interfaces are used locally and in Azure. Local `.env` files are never committed. Production secrets are retrieved from Key Vault.

### 11.3 Environment separation

Development and production use separate Entra app registrations and redirect URIs. Local redirect URIs are restricted to development. Production redirect URIs contain only approved Atea service domains.

The deployment pipeline must run database migrations, configuration validation, security checks, and smoke tests before making a release available to customer workspaces.

## 12. Atea design requirements

The interface uses the Atea digital foundation:

- Inter typography loaded from bundled licensed font assets
- Atea logo artwork, never typed or traced
- Green for primary actions
- Neutral grey and white surfaces for structure
- Teal for identifiable links
- Secondary colours reserved for status and data visualization
- Flat surfaces and dividers instead of decorative gradients or excessive shadows
- Explicit focus styles and semantic status indicators
- Light theme as default, with tested dark semantic mappings

The interface must be checked at desktop and narrow widths. It must support keyboard navigation, readable focus, long translated strings in future, reduced motion, and accessible errors.

## 13. Testing and acceptance criteria

### 13.1 Unit tests

Unit tests cover:

- Tenant/workspace context resolution
- Capability states and role combinations
- PIM state transitions
- Graph error mapping
- Retry and throttling policy
- Audit redaction
- Idempotency behavior
- Configuration validation

### 13.2 Integration tests

Integration tests use a controlled Microsoft 365 test tenant and cover:

- Customer admin onboarding and consent
- Atea B2B guest onboarding
- Global Reader read-only behavior
- User Administrator lifecycle behavior
- Missing consent
- Missing active role
- Eligible but inactive PIM role
- Active PIM role
- Approval-required PIM
- MFA/Conditional Access challenge
- Consent revocation
- Graph throttling and transient failure

### 13.3 End-to-end tests

Browser tests cover:

- Onboarding states
- Workspace overview
- User search, filters, pagination, and refresh
- User creation and confirmation
- User editing
- Disable/reactivate
- Group membership changes
- License assignment/removal
- Hidden sections and disabled actions
- Permission explanations
- Light/dark mode
- Keyboard and focus behavior

### 13.4 Security tests

Security validation covers:

- Cross-tenant object access and IDOR attempts
- Arbitrary tenant switching
- Unauthorized API mutations
- Token and secret leakage in logs
- Session fixation and cookie flags
- CSRF protection
- Content Security Policy and secure headers
- Replay or duplicate mutation submission
- Workspace membership removal during an active session

### 13.5 Performance checks

The first release must support a medium-sized tenant with approximately 500–1,500 users and regular administrator usage. Tests must verify paginated directory performance, bounded Graph concurrency, and graceful behavior under throttling.

## 14. Definition of success for the first pilot

The pilot is successful when:

1. Atea can provision and invite a customer workspace.
2. A customer administrator can connect a test tenant through delegated consent.
3. An Atea B2B guest can connect or operate only with customer-granted access.
4. Global Reader can view permitted directory information but cannot mutate users.
5. User Administrator can perform the approved lifecycle actions.
6. Users with inactive eligible PIM roles receive a useful activation path.
7. Denied actions are enforced by the API and explained by the UI.
8. No cross-tenant data access is possible in security tests.
9. The same build runs locally and in Azure without code-level environment forks.
10. The UI meets the approved Atea light/dark design direction and accessibility checks.
