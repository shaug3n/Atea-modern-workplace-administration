# F6: About, Changelog, System versions and Feedback

## Intent and success criteria

Provide trustworthy product information, shipped release history and deployed build visibility, plus a private way for authenticated workspace members to save feedback and read their own submissions.

This feature is one independently owned branch in the broader product work. It must follow `docs/contributing/feature-extension-contracts.md`, preserve W0 primitives and existing capability/audit gates, and avoid sibling feature ownership. Do not access `prism.orklait.no`.

Success means real, permission-gated pages rather than placeholders; build values that describe the deployed application; only authorized app routes in System versions; and feedback persisted in the application database without external delivery.

## Approved scope

- About tabs: Overview, Security & compliance, Architecture, Privacy and Changelog.
- Changelog filters: All, New, Improved and Fixed, with textual entry-type chips.
- System versions: deployed web/API build metadata and authorized application route inventory.
- Shell: a compact version, commit and branch build chip, plus a feedback floating action button.
- Feedback: submission form, shared accessible dialog, keyboard shortcut, friendly empty state and own-submissions list.
- Capability activation for existing authenticated workspace roles, preserving current workspace and authorization checks.
- Feedback retention of 90 days in the active database.

Out of scope: AI assistant, external feedback delivery, support-system integration, reviewer access, review/status-management workflow, attachments, screenshots, logs, automatic diagnostics, configurable content management, and changes to sibling feature pages.

## Architecture and ownership

Use two focused modules. About owns repository-backed product/release content and build/system presentation. Feedback owns submission persistence, reading and expiry cleanup.

Owned frontend directories are `src/Web/src/features/about/` and `src/Web/src/features/feedback/`. Owned backend additions live in `src/Api/Features/Platform*/` or `src/Api/Features/Feedback/`. Tests follow the existing Web.UnitTests, Api.UnitTests and Web.E2E layouts; persistence integration tests may be added where existing patterns require them.

Registration and composition edits are minimal and additive. Activate W0-reserved module identifiers `about` and `feedback`, and reserved capabilities `platform.about.view` and `feedback.submit`, according to the extension contracts. Candidate identifiers are not existing grants.

Do not modify W0 primitives in `src/Web/src/components/`. AppShell insertion is the explicitly permitted narrow exception: separate small blocks for the build chip and feedback entry point. F7 owns the account-summary/user-menu slot; F6 must leave that slot unchanged.

Follow existing API feature registration, endpoint mapping, capability evaluation, workspace context, persistence migration and audit patterns. No new authorization framework or duplicate route registry.

## Access and data isolation

Deliberately grant `platform.about.view` and `feedback.submit` to existing authenticated workspace roles within current permission/workspace contracts. About and System versions share the About gate. Feedback submission and own-list access share the Feedback gate.

Every protected backend endpoint enforces authentication, capability and active-workspace checks. UI navigation visibility is not authorization.

Feedback reads always filter by both current workspace and authenticated submitter identity. Neither identity comes from user-controlled request fields. There is no cross-workspace, administrator-review or support-reader endpoint.

System versions derives its route inventory from the existing application route metadata and current authorization rules. It displays only routes the current user can access. It does not expose a raw backend endpoint inventory or unauthorized route names.

## About, Changelog and System versions

Use curated repository-backed content. Security and compliance copy describes verified controls; it must not imply certification or guarantees unsupported by the repository. Privacy copy describes actual application behavior and explicitly does not advertise an AI assistant.

Changelog entries represent shipped changes supported by repository history. Each entry has a release/date grouping, stable identity, type and plain-language description. Filters do not change entry order. Include a genuine no-matches state.

Build inputs supply product version, commit and branch consistently for web/API builds. Add Dockerfile or CI build arguments only where needed. Do not query git at runtime or substitute fabricated values. An unavailable field is explicitly labelled unavailable.

The shell chip uses deployed web build metadata. System versions labels web and API metadata separately so mismatched builds are visible. Route inventory means authorized frontend application routes, not tenant integration versions.

Reuse established Atea tokens, typography, layout and accessible W0 components. Preserve desktop and narrow-screen usability.

## Feedback data and flow

Form fields:

| Field | Requirement |
| --- | --- |
| Category | Required enum: Bug, Improvement, General |
| Subject | Required, non-whitespace text, maximum 120 characters |
| Message | Required, non-whitespace text, maximum 4,000 characters |

Validate on client and server using matching limits and enum rules. Store feedback as plain text and render it as text, not HTML.

Persist only an opaque submission ID, current workspace ID, authenticated submitter ID, category, subject, message and timestamps needed for creation and expiry. A retry-protection key is permitted solely to prevent duplicate writes; it expires and is deleted with its submission.

Before submit, show a privacy warning not to enter secrets or sensitive tenant/person data, the 90-day retention notice, and the fact that feedback is saved in the app without external delivery or a reviewer workflow.

The form, floating action button and keyboard shortcut share the same submission flow. Alt+Shift+F opens the accessible dialog when the user has permission. Ignore shortcut events from text-entry/editable controls and during IME composition. Verify collisions against existing app shortcuts before implementation; a collision requires a user decision rather than silently changing the approved shortcut.

The dialog uses an existing accessible primitive, labelled title, appropriate initial focus, keyboard containment, Escape handling and focus return. Its launch controls are explicitly labelled.

Pending state prevents repeated clicks. A bounded, submission-scoped retry token protects uncertain network retries from duplicate writes. A changed payload cannot silently reuse a previous successful request. Authentication or workspace changes must not reuse the prior context's retry state.

Errors preserve input, show actionable feedback and never appear as successful saves. Success says "Saved", not "Sent to support" or "Under review".

The Feedback page displays only the user's unexpired submissions in the active workspace, newest first, with stable ordering and 20-item pagination. Include explicit loading, error, empty and populated states. An empty state provides an action to submit feedback. There is no invented workflow status.

## Retention, audit and privacy

Compute expiry at creation as 90 days later in UTC using the repository's clock conventions. Reads hide expired records immediately, independently of background-job execution.

A scheduled, bounded cleanup deletes expired submissions and associated retry/feedback metadata from the active database. Cleanup is infrastructure-owned, not a user-callable unauthenticated endpoint. Use the repository's existing background execution and persistence patterns; failures must be visible through standard operational logging without feedback content.

Existing backup retention is separate. Document established backup behavior if evidenced; otherwise explicitly state that active-database deletion does not establish a backup-erasure guarantee.

Retain existing security-audit policy separately. Feedback content, subject and diagnostic payloads must not appear in audit events, logs or telemetry. Any required audit event contains only repository-standard minimal identity/action metadata. Do not create a third-party delivery path.

## Errors and reliability

Use existing API error response and client notification conventions. Validation, authorization, expired session, persistence failure, metadata retrieval failure and cleanup failures remain distinguishable where the existing contracts permit.

Do not return empty success-shaped results on transport or storage errors. Missing build inputs are a labelled metadata condition, not fabricated release information. Preserve real HTTP failures separately.

Use cancellable/bounded data access and stable pagination. Any existing rate-limit policy continues to apply; this feature does not introduce an unapproved product quota.

## Verification requirements

Web unit tests cover module/capability activation, tab and filter behavior, metadata rendering, authorized route inventory, form validation, pending/error/success states, shortcut exclusions, dialog focus and own-list pagination.

API unit tests cover endpoint authorization, active workspace requirements, server-derived identity, category/text validation, owner/workspace filtering, exact 90-day boundary behavior, retry protection and retention logic. Persistence integration tests exercise migration/query or transaction behavior when unit tests cannot establish those properties.

Owned-page E2E covers About tabs, Changelog filters, System versions, feedback launch from shell and shortcut, dialog focus return, successful save, server failure without lost input, and own-submissions loading/empty/populated states. Confirm unauthorized navigation and API access do not expose data.

Build/type-check and the smallest relevant existing suites must pass. No new testing stack solely for F6.

## Delivery and process

This written specification requires explicit user approval before detailed planning. Detailed planning uses GPT-6 Luna with high reasoning effort. Implementation uses GPT-6 Luna at low/medium reasoning effort, or explicitly permitted Claude Sonnet 5.5 agents; OpenAI remains the default.

Create a checkpoint containing the approved plan before executing. While plan mode remains active, this specification stays in session storage. After approval authorizes repository writes, copy the approved specification to `docs/superpowers/specs/2026-10-08-f6-about-feedback-design.md` and commit it with the required co-author trailer.

Keep one F6 PR targeting master. Rebase on master before opening the PR, preserve sibling work, and send the PR link to the coordinator once CI is green. Do not merge or approve cloud deployments.
