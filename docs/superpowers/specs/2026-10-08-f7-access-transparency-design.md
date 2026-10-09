# F7: Access transparency

## Intent and approved design

Help a signed-in workspace member understand which actions they can perform, why an action is unavailable, and which existing next step may unblock it. Access transparency must not become a new source of authority.

The user approved:

- A separate, read-only **My access** page for every signed-in workspace member.
- A compact **DOMAIN ACCESS** section in the account menu, with read/write summaries and action-level details on My access.
- Explicit mixed and unavailable evidence, rather than inferred permission.
- Only role and consent evidence relevant to API-evaluated actions, not a complete Entra role inventory.
- Existing consent/PIM guidance, existing request-access links, and refresh. No new request-submission workflow or automatic activation.

This spec is staged in session artifacts because plan mode forbids repository mutation. After implementation approval, persist it as `docs/superpowers/specs/2026-10-08-f7-access-transparency-design.md` and commit it.

## Architecture and authority

Use the existing `CapabilitySnapshot` and `CapabilityDecision` model, evaluated by the API. `/api/capabilities` is the existing capability read endpoint. Reuse current authenticated workspace selection, queries, and error conventions.

Build the page under `src/Web/src/features/my-access/`. Keep feature-owned display mapping and summary logic together, separate from permission checks. Shared components consume existing decision contracts; do not change W0 primitives.

The presentation mapping associates modules and read/write action groups with capability IDs that the API actually evaluates. It is descriptive, not an evaluator. It must not infer a module's full write access from one allowed action. An allowed summary means all mapped actions in that group are explicitly allowed and its declared coverage is complete. Different known outcomes are mixed. Missing decisions or incomplete coverage are unavailable/partial, never allowed or denied by assumption. Preserve individual reasons even when all actions are unavailable for different reasons.

Authentication campaigns is represented as a read-only module here: `authentication.campaigns.view` is API-evaluated, while campaign management remains reserved and is not presented as a decision.

Use `DomainAccessChip` only when its existing contract accurately represents the available decisions. Supplement it with explicit text for mixed/partial evidence; never fabricate an API decision to fit a pill. Unmapped modules show that coverage is unavailable, not a fake capability.

Workspace grants and Microsoft authorization are distinct:

- Workspace grant: the API-evaluated application/module access evidence.
- Microsoft consent: decision-provided missing scopes, consent reason, or next step.
- Entra roles and PIM: relevant role requirement and PIM state supplied by the API.

A required role template is not evidence that the user holds that role. Absence of missing scopes is not proof that all Microsoft consent is granted. Never describe evidence not present in the response as verified.

If the existing response cannot show the caller's workspace grants or relevant active-role evidence, add the smallest additive, read-only projection in the authorization feature using existing server evaluation/context. Preserve existing decision semantics and clients. Return explicit availability with evidence, rather than success-shaped defaults. Do not request new Graph permissions, invent role assignment data, or create a second policy engine.

## My access page

Route: `/my-access`, reached from the account menu. Do not repurpose `/workspace-access` or `/settings/access`; those remain grant-management surfaces.

Show current workspace and snapshot evaluation time/source status. Explain that application grants and Microsoft permissions are both relevant and the API enforces actions.

For each module, show a read/write summary and its API-evaluated action details. Each detail exposes the decision state, human-readable reason, workspace-grant evidence, relevant Microsoft consent evidence, role requirement/active evidence, PIM state, and supported next step. When a layer is not separately observable, label it unavailable rather than decomposing an aggregate decision by guesswork.

Prefer existing `PermissionState` and `DisabledReason` conventions. Access explanations must distinguish workspace access, missing consent, role/PIM restrictions, and unavailable evidence when the API distinguishes them. No changes to sibling pages or their action gates.

## Account menu

Add a My access link and compact DOMAIN ACCESS list in the existing account-summary/user-menu slot. Use the same snapshot and summary mapping as the page. Keep desktop and mobile account entry points consistent.

Use readable labels beside dot pairs so color is not the only signal. Preserve account identity, tenant controls, menu keyboard behavior, focus return, and existing logout actions. Keep loading and unavailable evidence visible without falsely showing no access.

Coordinator approved one additive route entry and a minimal additive `AppShell.tsx` edit limited to the account-summary/user-menu slot. F6 owns the feedback FAB and footer/build chip; do not touch those areas.

## Guidance, refresh, and failures

Expose only existing actionable next steps. Reuse `PimGuidedHandoff` where its existing role/next-step contract fits, without changing activation authority or existing audit gates. Opening existing external guidance does not mean activation succeeded.

Request access is a link only when an existing supported destination is available. Otherwise show actionable explanation, not a dead button or fabricated destination.

Refresh re-fetches workspace-scoped evaluated access. Handle workspace changes without displaying the previous workspace's access as current. Do not share access evidence across workspaces.

Show an explicit initial loading state, retryable API failure, source-unavailable/partial state, and empty evaluated coverage. During refresh, label retained evidence as previous/stale until replaced; failed refresh does not imply valid current access. Follow repository notification/error patterns.

## UI and accessibility

Reuse existing Atea tokens, typography, controls, and icon family. No new branding assets, framework, or dependencies are required by this feature.

Use neutral surfaces, clear section hierarchy, accessible state labels, visible keyboard focus, and narrow-screen reflow. Navigation uses links; refresh uses a button with busy feedback. Explanations must be available without relying on hover or a disabled element receiving focus.

## Ownership and exclusions

Owned: `features/workspace-access/**` only when required for reuse, new `features/my-access/**`, minimal `TenantContextHeader.tsx` account integration, authorization feature read projection if necessary, and related tests.

Approved shared integration: one route entry and one small account-summary block/prop in `AppShell.tsx`, following `docs/contributing/feature-extension-contracts.md`.

Excluded: W0 primitive changes, grant-management changes, sibling page placement, new write endpoints, new Graph consent requirements, complete role inventory, deployments, and merges. Do not access `prism.orklait.no`.

## Acceptance and verification

- Unit coverage for complete allowed, denied, mixed, missing, and incomplete read/write evidence; summaries never overstate authority.
- Page/menu tests for supported next steps, unavailable layer evidence, loading, API failure/retry, refresh, stale evidence, workspace switching, and desktop/mobile account access.
- Existing capability, PIM, workspace-access, and shell behavior remains unchanged.
- API tests for any additive projection: authentication, caller/workspace isolation, safe evidence availability, unchanged decision semantics, and no write side effects.
- Owned-page E2E coverage for menu navigation, My access rendering, workspace changes, error states, and supported guidance.
- Type/build checks and targeted `tests/Web.UnitTests` / `tests/Api.UnitTests` coverage follow existing scripts.
- Inspect the implemented owned UI at desktop and narrow widths with keyboard interaction; do not use the external Prism site.

## Delivery

After written-spec approval, create the detailed implementation plan using GPT-6 Luna with high reasoning effort. Request plan approval. Save a rollback checkpoint of the approved plan before execution.

Implementation uses GPT-6 Luna at low/medium effort or explicitly permitted Claude Sonnet 5.5 agents only. Keep other stages on OpenAI models. Rebase on `master` before opening one PR to `master`; do not merge or approve cloud deployment. Send the coordinator the PR link when CI is green.
