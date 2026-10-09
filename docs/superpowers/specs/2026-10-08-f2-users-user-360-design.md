# F2: Users directory and User 360

## Status and intent

Architectural design for review. This specification is staged in session storage because plan mode prohibits repository changes. After repository-write approval, publish it as `docs/superpowers/specs/2026-10-08-f2-users-user-360-design.md`.

Provide administrators a readable user directory and a complete user-centered account workspace. Preserve working operations, independently authorized sections, source-of-truth restrictions, and existing safety gates. Adapt the documented Prism interaction patterns; do not access Prism.

The user approved:

- Existing data only, with explicit unavailable states for unsupported signals.
- URL-backed built-in directory views without personal storage or tenant-wide counts.
- The profile structure described below.
- Required audited reasons for every F2 user-facing write and configurable, policy-enforced TAP options.

Written-spec approval and detailed-plan approval remain separate gates. This specification does not authorize implementation.

## Baseline and ownership

Inspection found a clean worktree based on `8f50646`, the merge of W0 PR #10.

Primary ownership:

- `src/Web/src/features/users/**`
- `src/Api/Features/Users/**`
- User-related `Groups/**` and authentication-method portions of `Identity/**`
- Corresponding Web.UnitTests, Api.UnitTests, integration tests, and owned-page E2E tests

Follow `docs/contributing/feature-extension-contracts.md`. Shared changes must be minimal and additive. Do not edit `src/Web/src/components/` or redesign another branch's feature. Coordinate required Graph adapter changes and shared reason-contract propagation. F3 owns device pages; F4 owns license/hygiene features; F5 owns MFA campaigns; F7 owns permissions/domain-access pages.

Coordinator rulings: F2 owns minimal additive changes to `Infrastructure/Graph/GraphAuthenticationMethodCommands.cs` and the group write path. Preserve existing TAP adapter signatures through overloads or optional parameters. F2 owns the user-side license dialog and endpoint; `Infrastructure/Graph/GraphLicenseService.cs` remains shared with F4, so reason propagation uses an optional parameter or overload without renames or parameter reordering. Reuse the existing audit write path and metadata; do not change the shared audit schema without coordinator approval. Feature-owned complex dialogs may compose `ConfirmationDialog`.

Read-only Graph operations remain read-only. Extend existing write operations only for the approved reasons and TAP controls. No additional Graph read-data source or permission scope is introduced.

## Directory

Retain search, supported predicates, continuation-token paging, refresh, filtered CSV export, and current action capability checks.

Replace filter clutter with built-in views: All users, Enabled, Disabled, and Guests. Use existing `KpiFilterTile` with no invented numeric value. These are filter shortcuts, not tenant KPIs. Selecting a built-in view resets conflicting view predicates and paging; preserve search and compatible advanced filters. All users clears account-status and user-type view predicates, not the search or license predicate.

Use existing supported URL parameters for search, account status, user type, and license. Back/forward navigation restores filters and resets paging consistently. Preserve the debounce and protect against stale responses. Keep secondary supported filters in a compact advanced-filter region. Tenant-role filtering remains unavailable until a real supported index exists.

Do not add saved-view storage, global totals, inferred percentages, or counts derived from a page and presented as tenant totals. Label any displayed result count explicitly as the current page.

## User 360 structure

The always-visible area contains:

- Profile header with identity, domain-access chip, account status, and source-of-authority information.
- Compact summary strip using independently available existing data.
- Quick actions with the audit-logging explanation and adjacent disabled reasons.

Use `DomainAccessChip` for actual view/write decisions, not for missing telemetry. Preserve its hidden-decision semantics and do not expose denied identity data through the header.

Summary data includes account state, known authentication methods, and license assignments where authorized. Authentication-method availability is not proof of MFA registration, enforcement, or compliance. Phishing and last sign-in show explicit unavailable text; unknown values are never displayed as zero, false, or "never."

Tabs:

- **Identity:** identity/job cards, groups, roles/PIM, licenses, and authentication methods.
- **Devices:** existing user-associated device section and existing navigation links only.
- **Activity:** honest explanation that this workspace currently has no supported activity source. No fabricated feed, sample records, or empty-feed implication.

Keep stable headings, semantic tab controls, keyboard navigation, and correct focus behavior. Do not add an editable role-management surface where none exists.

Use a restrained accent-card grid built from existing Atea tokens. Per-card Edit opens an existing relevant editor, without creating duplicate mutation implementations. Field-level source locks stay field-specific: on-premises or HR synchronization does not automatically prohibit unrelated cloud security actions.

## Loading and failures

Each independently loaded section keeps its own busy, success, empty, denied, unavailable, and failure states. Use existing `LoadingSkeleton`; mark the containing region `aria-busy`. A failure in licenses, devices, groups, or authentication methods must not erase a usable profile.

Keep meaningful retry controls and existing throttling/error semantics. Do not initiate nested module reads when the caller lacks the relevant module grant. Avoid refresh loops. Successful mutations invalidate only the affected existing queries, plus related summary data.

## Write safety and reasons

Every user-facing write initiated in F2 requires a reason: create/edit, enable/disable, password reset, session revocation, group membership changes, user-side license changes, authentication-method removal/reset, and TAP creation. Apply the same rule to directory and profile entry points.

Proposed validation, subject to written-spec approval: trim the reason, require a nonblank value, and cap it at 1,000 characters on client and server. Treat it as sensitive operational free text; render it as text, never HTML. It is not a password field or a place for secrets. Include an actionable example in the dialog.

Carry the validated reason through the actual command path into existing safe audit metadata for success and failure entries. Do not show a reason field that is discarded. Preserve audit failure warnings and the repository's existing behavior when the audit store fails. Never log passwords or TAP secrets.

Retain workspace-module authorization, capability requirements, Microsoft role/PIM checks, source-of-truth locks, idempotency, and Graph enforcement at every write boundary. A visible disabled explanation is not an authorization mechanism.

Use W0 `ReasonDialog` for simple confirmations. Its props deliberately omit custom children, so complex existing forms and TAP options must compose feature-owned content through existing dialog primitives. Do not change W0 or duplicate modal/focus mechanics. Prevent double submission, retain entered values after an error, and return focus appropriately.

Additive changes to shared group/license contracts must retain compatibility with callers outside F2. F2 user entry points always require a reason, but do not silently make unrelated branch flows invalid. Apply the coordinator's ownership rulings above; escalate any need for a shared audit schema change before implementation.

## Temporary Access Pass

The baseline Graph adapter and service require exactly 60 minutes and single-use. Supporting options therefore requires coherent changes to the request contract, Graph adapter, success validation, UI, and tests.

Defaults remain 60 minutes and single-use. Provide 1h, 8h, and 24h presets, a labelled minutes input/stepper, and a one-time toggle.

Proposed product range, subject to written-spec approval: 10 through 1,440 whole minutes, with a 10-minute stepper increment. Presets select 60, 480, or 1,440. Directly entered whole-minute values within the product range are valid; the stepper increment is not a divisibility rule.

These controls express a request, not guaranteed tenant support. Graph remains the tenant-policy authority; no extra policy-read scope is added. Translate tenant-policy rejections into explicit actionable failure states, preserving input and the required reason. Do not retry with changed options or silently clamp values.

Validate returned lifetime and usage settings against the accepted request, not hard-coded baseline defaults. Preserve actual expiry/result information and the current one-time secret display. Idempotent replay never re-displays the secret. Keep the secret out of audits, URLs, browser persistence, safe idempotency result storage, and telemetry. Reusing a key for a different payload must follow the existing idempotency conflict policy, not return success for a different request.

## Accessibility and branding

Reuse the existing Atea component/token system; no new framework, font, shell, or branding package. Neutral surfaces and readable text dominate; accents communicate hierarchy or state.

Disabled actions must have readable, keyboard-discoverable reasons using `DisabledReason` where suitable, including source-of-truth explanations. The quick-action audit statement describes audit attempts; actual audit failures remain visible rather than being hidden by an absolute guarantee.

Cover desktop and narrow layouts, zoom/reflow, long values, visible focus, labels, validation announcements, tab navigation, dialog focus return, and independent busy regions. Do not use color alone for status. Follow feature-extension-contract localization requirements and current typed messages; avoid inline product copy.

## Validation and acceptance

Add focused behavioral tests for:

- Supported built-in views, URL restoration, paging reset, compatible filters, and no misleading global counts.
- Header/status/summary semantics, tab behavior, per-card editor routing, source locks, module/capability-denied actions, and independent loading/failure states.
- Explicit unavailable activity, phishing, and last-sign-in states; no inferred MFA-compliance claim.
- Required reasons at every F2 write entry point, server rejection of blank/oversized input, audit propagation, audit warnings, and safe metadata.
- TAP defaults, presets, input bounds, one-time toggle, request/response option mapping, policy rejection, replay secrecy, and differing-payload idempotency behavior.
- Unchanged capability/PIM/Graph gates, nested module gates, and correct Graph request bodies.

Use Web.UnitTests behavior coverage, Api.UnitTests and relevant integration tests, plus users-directory/user-detail E2E coverage. Build/type-check the web app and compile affected API/tests using repository commands. Verify actual owned-page layouts and keyboard flows with browser tooling during execution. New test tooling is unnecessary.

Acceptance: existing user flows remain usable and authorized; reasons persist into the real audit path; TAP options reach Graph safely; unsupported signals remain honest; no W0 component or sibling feature redesign.

## Workflow and exclusions

After written-spec approval, use GPT-6 Luna with high reasoning for the detailed implementation plan. Save that plan in the session's `plan.md`, record todos/dependencies in SQL, and request plan approval.

Before execution, create a persistent checkpoint summarizing the approved plan and current worktree baseline. Implementation runs only on GPT-6 Luna low/medium or user-authorized Claude Sonnet 5.5 subagents; prefer the requested OpenAI pipeline. Preserve all unrelated changes.

Rebase on master before opening one PR to master. Do not approve deployments or merge. Notify the coordinator with the PR link after CI is green.

Out of scope: new activity/phishing/sign-in data sources, global-count infrastructure, persistent saved views, device-page changes, MFA campaign changes, license/hygiene redesign, domain-permissions pages, and shared W0 component changes.
