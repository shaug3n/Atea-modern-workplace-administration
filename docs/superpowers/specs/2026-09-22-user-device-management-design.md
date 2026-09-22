# Atea Unified Workplace — User and Device Management Design

**Date:** 2026-09-22  
**Status:** Approved direction inferred from the requested continuation; Approach 1 (command-centred detail experience)  
**Scope:** Atea-branded UI refinement plus delegated Microsoft Entra and Intune management capabilities for the local real-tenant MVP. Hosted Azure deployment remains out of scope.

## Outcome

The product must make routine administration feel deliberate rather than crowded. A tenant administrator must be able to inspect a user, understand their sign-in and device posture, and perform only the management actions that their existing Entra roles and granted Graph consent permit. A Global Reader remains read-only; an eligible but inactive PIM user receives an activation path or a clear guided handoff; an authorized administrator receives the actionable controls.

The completed local MVP provides:

1. A consistent Atea-style page, card, action-bar, table, and destructive-action system with materially more whitespace and no positional button styling.
2. A user blade with profile, access, security, and associated Intune-device information arranged by operational task.
3. Supported user-security actions: reset password, reset all removable MFA methods, remove one authentication method, grant a Temporary Access Pass (TAP), revoke sessions, disable, and reactivate.
4. Supported device actions: sync, remote lock, restart, retire, and wipe, presented only when the tenant configuration and the signed-in administrator's RBAC permit them.
5. API enforcement, idempotency, safe audit events, useful capability states, and deterministic test coverage for every management action.

## Design direction

### Chosen approach: command-centred detail experience

The user detail page is a short operational hierarchy rather than an undifferentiated grid of boxes:

- The **identity hero** contains only the return link, person identity/status and one ordered page action bar.
- The **action bar** groups high-frequency commands: Edit profile, Reset password, and an `Actions` menu. Contextual security actions remain in the Security card, not beside profile editing.
- The first content row is **Profile** and **Security posture**. Profile consolidates the existing identity and job information. Security posture summarizes account state, active roles/PIM state, registered method count, and device count.
- The second row is a wide **Sign-in security** section and a wide **Associated devices** section. They are the two decision-heavy areas and must not compete with static identity fields.
- The final row groups **Access**: licenses, group membership, and roles/PIM. These retain their existing working mutations but use consistent card actions.

Device management uses the same vocabulary. The devices page has a clean page header, summary cards, a spacious filter bar, a table with `Open details` plus one `Actions` menu per row, and a detail panel divided into Overview, Security, and a dangerous action zone. There are no three inline row buttons competing for attention.

### Layout and visual system

The existing Atea palette, Inter typography, light/dark theme support, green primary action and neutral surfaces remain. The implementation standardizes the current styling instead of replacing it with a new visual language:

- Base spacing follows the existing 8px scale. Page sections use 32px separation; sibling cards use 24px; card interior padding is 24px desktop and 16px narrow; header metadata/action groups use 12px gaps.
- A page's maximum readable detail width is explicit. Wide tables are allowed to use the available content width without squeezing card content.
- Every card has one header anatomy: title and supporting freshness/status on the left; optional contextual actions on the right; clear body separation below.
- Buttons use explicit semantic classes: `primary`, `secondary`, `quiet`, and `danger`. Existing `:first-child`/`:last-child` selectors must not decide command importance.
- Destructive actions use red labels and are never the default primary button. Retire and wipe are only available from the dangerous device-action zone after a typed confirmation.
- Row-level controls use an overflow action menu. Keyboard access, focus return, Escape dismissal, busy states, and accessible labels are mandatory.

## Capability and RBAC model

The API remains the authorization authority. The frontend reflects the capability decision but never treats a visible control as authorization.

| Capability state | UI behaviour |
| --- | --- |
| `allowed` | Show the action and run the corresponding API call. |
| `read_only` | Show the information and an explanatory read-only notice; do not show mutable controls. |
| `pim_required` / `approval_required` | Show the existing PIM activation or guided handoff control at the affected section. |
| `consent_required` | Do not attempt the mutation. Surface the existing workspace consent path and the exact missing permission. |
| `hidden` | Do not render tenant-sensitive sections or actions that the user cannot view. |

The app must use the signed-in user's delegated Graph token. It must not introduce an app-only bypass, copy tenant privileges into the local database, or allow Atea platform membership to elevate a customer-tenant role.

## User management

### User security actions

The `Authentication methods` section becomes the **Sign-in security** card. It keeps the existing live method list and adds:

- **Reset MFA methods**: removes all removable methods, preserves the password method, requires `authentication.methods.manage`, a confirmation that spells out the effect, an idempotency key, audit event and list refresh.
- **Remove method**: retains the existing per-method removal flow. Password remains non-removable. A TAP may be revoked by removing its method record; no TAP secret is ever shown again.
- **Grant Temporary Access Pass**: creates a Microsoft Graph v1.0 temporary access pass under `authentication.methods.manage`. The fixed local-MVP profile is **single-use, valid for 60 minutes, available immediately**. The returned passcode is shown exactly once in a copy-enabled success dialog, is not persisted in PostgreSQL, browser storage, telemetry, audit metadata, or application logs, and disappears on dialog dismissal. The UI explains that the tenant's Authentication Methods policy can reject the request and translates Graph's failure into a policy/permission message.
- **Revoke sessions**: invalidates the selected user's refresh tokens and browser sessions through Graph. The confirmation states that sign-in can take several minutes to be fully invalidated and that external users' home-tenant sessions are unaffected. This is distinct from password reset.

Existing profile edit, password reset, disable/reactivate, group membership and license flows remain supported. Passwords and TAP codes are never placed in audit data, error messages, or test fixtures.

### Associated Intune devices

The user blade gains an `Associated devices` section that reads `managedDevice.userId` and shows, for every returned device:

- Device name, platform and OS version.
- Compliance and management state.
- Company/personal ownership.
- Last successful Intune check-in.
- A compact `Open device` link that opens the existing device detail panel, and available management actions only when their capability is allowed.

Microsoft Graph documents `userId` as the associated-user property of `managedDevice`, but does not document a v1 server-side filter for it. The local MVP therefore uses a narrowly scoped, real-tenant-tested filter query (`userId eq '{userObjectId}'`) in a dedicated association reader. The implementation must never fall back to traversing every device page during a user-detail request. If the tenant rejects the filter, the section returns a truthful `unavailable` association state with an admin-facing diagnostic. A hosted-scale read model or a documented Graph alternative is a later architectural addition, not a hidden unbounded local workaround.

## Device management

### Action classes

All device commands are auditable, idempotent mutations. Each has a pending state and refreshes the device page after the command is accepted. Tenant/Graph execution is asynchronous, so the UI communicates `request submitted` rather than asserting the device immediately completed the command.

| Action | Presentation | Confirmation |
| --- | --- | --- |
| Sync | `Actions` menu | Standard confirmation explaining that Intune will request a sync. |
| Remote lock | `Actions` menu / Security card | Standard confirmation. |
| Restart | `Actions` menu / Security card | Standard confirmation that a user can be interrupted. |
| Retire | Dangerous action zone | Typed `RETIRE` confirmation; explains that corporate data and management are removed. |
| Wipe | Dangerous action zone | Typed `WIPE` confirmation; explains that this factory-resets the device and sends the Graph default wipe parameters. |

Actions must use Microsoft Graph v1.0 endpoints only. No destructive browser-based live test is required or permitted. The UI and service are covered by fixtures, unit/integration tests, and an optional tenant test limited to non-destructive `sync` when a dedicated disposable device is available.

### Device permissions

`DeviceManagementManagedDevices.Read.All` continues to enable the device inventory. Microsoft Graph requires the more privileged delegated scope `DeviceManagementManagedDevices.PrivilegedOperations.All` for sync, remote lock, reboot, retire, and wipe. The app therefore introduces a distinct `devices.privileged.manage` capability rather than treating `DeviceManagementManagedDevices.ReadWrite.All` as sufficient. A tenant administrator must grant the new scope and the acting user must hold a suitable Entra/Intune administrative role before the actions become available.

## New Graph permissions and consent guidance

The existing authentication-method read/write permission covers TAP creation and method removal. This scope change is required for the newly completed functions:

| Scope | Why | Local test-tenant action |
| --- | --- | --- |
| `User.RevokeSessions.All` | Revoke a selected user's sessions. | Add delegated permission to the API registration and grant tenant admin consent. |
| `DeviceManagementManagedDevices.PrivilegedOperations.All` | Sync, remote lock, restart, retire and wipe a managed device. | Add delegated permission to the API registration and grant tenant admin consent. |

The workspace consent page and Entra registration documentation must describe both scopes. Existing tenant consent and user RBAC still control whether they are usable.

## API contracts and safety rules

- Create `POST /api/users/{userObjectId}/authentication-methods/temporary-access-pass` with no client-supplied duration or reuse flag in the local MVP. Its success response includes the passcode once and must use a response type that cannot be accidentally logged as a general command result.
- Create `POST /api/users/{userObjectId}/revoke-sessions` behind a `users.sessions.revoke` capability and `User.RevokeSessions.All` scope requirement.
- Create `GET /api/users/{userObjectId}/devices` returning a user-scoped device response with freshness, partial-data state, capability decision and a safe diagnostic category. It must not expose raw Graph errors or token details.
- Extend the existing device action endpoint with `restart`, `retire` and `wipe`; move all device operations, including existing sync and remote lock, behind `devices.privileged.manage`.
- Validate route IDs and action names, require an idempotency key for every mutation, enforce API capability checks, map Graph failures safely, and write an audit event containing action, target, outcome and Graph request/correlation IDs only.
- TAP values, passwords, reset credentials, Graph access tokens, refresh tokens, client secrets, and raw Graph response bodies are prohibited from persistence and logging.

## Acceptance evidence

The implementation is complete only when all of the following are demonstrated:

- User and device views render with the specified action hierarchy, whitespace, accessible controls and correct light/dark styling at desktop and narrow widths.
- The user page displays a live associated device for the enrolled test-tenant device when the tenant supports the scoped association query; a rejected filter gives a specific unavailable state without a broad inventory scan.
- MFA reset, individual non-password method removal, TAP creation/reveal-once, session revocation, profile/password/disable/reactivate operations all honor delegated capabilities, idempotency and audit redaction.
- Global Reader remains unable to mutate; authorized administrator and eligible PIM scenarios receive correct allow, blocked or activation/handoff states.
- Device actions do not render as permitted without `DeviceManagementManagedDevices.PrivilegedOperations.All`; their confirmations and API command mapping are fully covered. Retire and wipe are not executed against a real non-disposable endpoint.
- API unit/integration tests, web unit tests, web build, and the local Docker compose health contract pass. The manual runbook identifies the two new Entra delegated permissions and safe tenant test steps.

## Non-goals

- Replacing the existing Atea visual identity, theme preference, local workspace/onboarding model, or delegated-RBAC architecture.
- Adding a hosted background device-sync service, application permissions, or an undocumented tenant-wide scan to simulate association data.
- Executing a destructive retire or wipe against the user's everyday test device.
