# F1: Role-aware Overview

## Intent and success criteria

Help workspace operators understand their effective access, the scope of displayed data, and their next useful action. Improve the existing Overview rather than adding a parallel dashboard.

The user approved:

- Effective capabilities plus workspace role for "You are," with PIM guidance.
- Numeric totals only when tenant-wide visibility is verified. Suppress totals for restricted or unverified scope.
- Deterministic, read-only priority actions: blocked access/PIM first, stale or partial data second, supported navigation last.
- Existing authorized app audit events only; no tenant-wide Graph audit feed.
- Extending the existing Overview API, with independently authorized source sections.
- The presentation and behavior described below.

Success means every visible metric has an authorized source, every destination works, and restricted or unavailable data cannot appear as zero, fresh, or tenant-complete.

## Scope and ownership

Owned implementation: `src/Web/src/features/overview/**`, `src/Api/Features/Overview/**`, and corresponding Web/API unit and owned-page E2E tests.

Follow `docs/contributing/feature-extension-contracts.md`. Shared integration files permit only minimal additive edits. Reuse `InfoTip` and `DataFreshness`; do not modify `src/Web/src/components/`. For Overview's authorized KPI destinations, the coordinator approved feature-local native linked cards instead of the shared toggle button, with no new generic primitive.

Do not implement Users/User360, Devices/Device360, Licenses/Hygiene, MFA campaigns, About/Feedback, Access transparency, or the later Exchange/Activity/Settings sweep. Do not access prism.orklait.no. Do not deploy or merge.

Keep all Graph operations read-only and preserve capability, workspace-module, PIM, and audit gates.

## Existing evidence

W0 PR #10 is merged; inspected HEAD is `8f50646`.

`GET /api/overview` currently reads user and license-assignment summaries, permission health, PIM attention, and freshness. The service uses a 30-second workspace cache with stale fallback. Its combined data read is gated by `users.view`; independent license gating is required before displaying license data.

The existing Graph reader counts users and users with assigned licenses. It does not provide a purchased-seat total or reliable device total. `AvailableLicenses = 0` must not become a seat-inventory KPI.

Graph authorization snapshots include role assignment state and directory scope. `CapabilityEvaluator` evaluates `users.view` and `licenses.view` independently; eligible PIM roles are not active grants. Workspace role is not proof of tenant-wide Graph access.

`/api/session` exposes workspace role and module access. `/api/capabilities` exposes effective decisions, PIM guidance, and unavailable authorization states.

Existing audit persistence stores app events. `GET /api/audit/events` requires `audit.view`, filters workspace and tenant/actor tenant, and binds pagination to the requester and workspace. This is not comprehensive Microsoft 365 history.

Users supports selected URL filters; Devices and Licenses do not currently hydrate general filters from URL. F1 will not invent deep-link query contracts.

## Architecture and alternatives

Extend the existing Overview endpoint with independently gated sections and a bounded safe audit projection. Keep composition, DTOs, source-state mapping, priority ranking, and feature copy inside Overview.

Rejected alternatives:

1. Browser-side composition across feature APIs: more requests, inconsistent source freshness, and extra feature coupling.
2. A new dashboard aggregation subsystem: unnecessary abstraction and parallel-branch merge risk.

Reuse existing authorization evaluators and persistence services. Do not alter shared Graph readers merely to split Overview data. If independent reads require a new reader, create an Overview-owned adapter and register it additively. Preserve other consumers.

Before finalizing the implementation plan, verify exact DTO and DI contracts. Any change to shared authorization semantics requires coordinator agreement, not a local bypass.

## Authorization and scope

Evaluate each section against the current authoritative authorization snapshot and effective workspace modules before returning data, including cached data.

- User totals require the Users module, an allowed `users.view` decision, and verified tenant-wide visibility.
- User-based license coverage requires the Licenses module, an allowed `licenses.view` decision, verified tenant-wide visibility, and every Graph scope needed by that specific count query. Do not substitute `users.view` for license authorization.
- Recent app activity requires allowed `audit.view` and all current audit isolation predicates. Do not broaden access relative to the audit endpoint.
- PIM eligibility provides guidance only. Unknown, unavailable, scoped, and blocked authorization never grant access.
- "You are" names the workspace role and describes effective access. It does not infer an Entra persona.

Use authoritative snapshot role scope and existing evaluator semantics to prove tenant-wide access. Do not equate workspace administrator status, assigned modules, or an eligible role with tenant-wide Graph visibility.

The scope presentation is section-aware. It must explain which totals are verified and which are suppressed, rather than implying every source has identical scope.

## Presentation

Retain the existing route and Atea design system.

Order:

1. "You are / Scope / Data freshness" context.
2. Source-backed KPI tiles.
3. Ranked priority actions.
4. Recent app activity.

For supported KPI navigation, use one native link per verified card with an accessible name containing its value and destination. This coordinator-approved feature-local linked-card exception replaces the shared `KpiFilterTile` toggle semantics because these cards navigate rather than select; retain existing card styles/tokens where possible and do not add a generic primitive. Bind links only to real, authorized routes. Do not introduce local filtering without a defined useful result. Disabled or blocked summaries do not masquerade as working links.

Present total users and user-based assigned-license coverage only when their contracts permit it. Explain the coverage denominator and scope with `InfoTip`; never call this purchased-seat usage. Do not add device totals, security scores, or inferred tenant-health metrics.

Rank actions deterministically by access/PIM, then freshness/partial-data, then supported navigation. Use urgency styling with readable labels so color is not the only signal. Stable tie-breaking preserves order across renders.

Show at most five recent app events, newest first with deterministic tie-breaking. Expose only safe action, outcome, and timestamp fields. Omit raw metadata and correlation identifiers. Label the source and explain that it records app activity, not all tenant changes.

Use existing feature localization composition. Preserve neutral surfaces, compact dashboard hierarchy, keyboard focus, accessible labels, and narrow-screen layouts.

## Freshness, caching, and errors

Keep the existing 30-second summary-cache policy. Isolate source states sufficiently that an audit failure cannot hide successful counts, or one successful read cannot mark another source fresh.

Each section reports its actual source fetch time and fresh/stale/unavailable or restricted state. Preserve the original fetch time on stale fallback. Do not replace it with response time.

Recheck authorization before serving cached data. Denied sections must not reuse authorized cached values. Cache identity and invalidation must prevent cross-workspace, cross-tenant, or authorization-context disclosure.

If a source read fails, expose an explicit unavailable/partial state using repository-standard logging and safe error reporting. Existing eligible stale fallback remains visible as stale. Never convert failures, denied access, or missing values to zero.

Retain retry. Preserve the Overview request boundary: the page should not independently fetch connection health or device search data. Recent app activity is part of the Overview response, not a new browser request waterfall.

An authorized empty audit result is an empty state. A denied or failed audit result is a distinct access/unavailable state.

## Verification requirements

API tests:

- Independent user/license authorization and module gates.
- Verified tenant-wide versus scoped, eligible PIM, and unknown authorization.
- Graph-scope requirements for actual count queries.
- Cache freshness, stale fallback, access revocation, and tenant/workspace isolation.
- Independently failing sources and no failure-shaped zero values.
- Audit capability, existing isolation predicates, safe projection, five-item bound, and stable ordering.

Web tests:

- Role/scope/freshness context and truthful license copy.
- Numeric suppression and distinct restricted, empty, stale, partial, and unavailable states.
- Deterministic priority order and supported navigation.
- No guessed device/license filters or sibling feature placeholders.
- Existing single-endpoint request boundary.
- Keyboard interactions and accessible explanatory text.

Owned-page E2E:

- Authorized Overview, restricted/PIM guidance, stale/partial behavior, retry, and audit states.
- Working destinations, keyboard focus, and narrow layout.

Use existing runners. Inspection identified:

`dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~Overview`

`npm --prefix src/Web run test:behavior`

`npm --prefix src/Web run build`

`npm --prefix tests/Web.E2E test`

The detailed plan must confirm targeted selectors, API build coverage, and E2E fixture support rather than assuming these broad commands are the smallest suitable checks.

## Approval and delivery

This is a session draft because plan mode prohibits repository mutations before approval. Intended persisted path: `docs/superpowers/specs/2026-10-08-f1-role-aware-overview-design.md`.

Written-spec approval permits detailed planning only. GPT-6 Luna with high reasoning writes the implementation plan. Explicit plan approval and a persisted checkpoint must precede implementation. Implementation runs only on GPT-6 Luna low/medium or explicitly permitted Claude Sonnet 5.5.

After implementation, persist the approved spec, validate the owned changes, rebase onto current master, and open one PR to master. Do not merge or approve deployment. Send the coordinator the PR link once CI is green.
