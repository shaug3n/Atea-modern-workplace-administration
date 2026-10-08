# F4: Licenses and License Hygiene

Intended repository location after plan approval: `docs/superpowers/specs/2026-10-08-license-hygiene-design.md`.

## Intent and approved scope

Help workspace administrators review license allocation with explainable evidence, rather than infer reclaimable seats or financial savings. Preserve the existing inventory, assignee, export, and license-assignment workflows.

The user approved:

- Review-only v1, with no reclaim actions.
- Verified available capacity and licensed disabled-account findings only.
- Explicit disclosure that inactivity and entitlement-overlap evidence is not supported.
- A separate, opt-in License Hygiene page, linked from Licenses when authorized.
- A scan limit of 10,000 user records per refresh, with truthful incomplete-coverage messaging.
- Explainer-first presentation, KPI filters, friendly SKU names with original identifiers, common-snapshot filtering, and owned-page tests.
- No new Graph permissions, hygiene exports, persistent scan storage, or changes to existing write gates.

## Boundaries

Feature implementation lives under `src/Web/src/features/licenses/**`, including a hygiene subfolder, and `src/Api/Features/Licenses/**`, plus owned tests. Shared integration changes are minimal and additive under `docs/contributing/feature-extension-contracts.md`.

Do not modify W0 components in `src/Web/src/components/`. Do not access the source scouting site. Other feature branches, Copilot credits, and Cowork credits are outside scope.

The existing SKU-name resolver remains the source of friendly names. Unknown SKU codes remain identifiable without invented names or entitlement mappings.

## Access and activation

Activate the reserved `licenses.hygiene.view` capability and `license-hygiene` module deliberately. Add a real route, navigation metadata, API checks, module registration, capability requirements, feature-owned copy, service registration, endpoint mapping, and focused tests together.

Existing workspaces and memberships receive no automatic hygiene grants. Hygiene access requires its enabled and assigned module, current workspace membership, and an authoritative capability decision. Loading, unavailable, stale-workspace, or denied capability snapshots must fail closed.

Use the existing license-read permission and role policy as the starting contract: delegated `Directory.Read.All` and the supported active read roles already used by license inventory, including License Administrator. No assignment or reclaim authority follows from hygiene access. Eligible roles retain existing PIM handling. Microsoft Graph remains the final source authorization boundary.

Reuse source readers/helpers without bypassing a service's existing module or capability checks. Where existing services enforce the Licenses module, either retain those checks or use their lower-level read adapter behind the new explicit hygiene gate. Do not weaken existing Licenses access to make hygiene work.

Validate the projected fields and read policy against Microsoft's documented Graph contract during detailed planning. Live tenant validation is not implied by unit tests or local capability approval. Record any unavailable tenant validation in the PR.

## Data architecture

A feature-owned read-only service assembles one authorized refresh snapshot:

1. Read subscribed SKUs using the existing inventory Graph reader and name resolver.
2. Read paged users through a feature-owned lean Graph projection containing `id`, `displayName`, `userPrincipalName`, `accountEnabled`, and `assignedLicenses`.
3. Derive SKU-capacity rows from verified subscription counts.
4. Derive one account-review row per observed disabled account with assigned licenses. Include the assigned SKU evidence, rather than duplicating account rows for every SKU.
5. Return independent source status, source timestamps, scan coverage, evidence gaps, and explicit errors alongside verified results.

The existing directory reader does not select `assignedLicenses`; do not silently assume its current user DTO contains assignment evidence. Avoid scanning each SKU's assignees separately or introducing per-user detail requests.

The proposed user-scan budget is 10,000 user records, 100 Graph page requests, or 30 seconds elapsed, whichever comes first. Request up to 100 users per page. The time budget includes Graph request and retry time, not just gaps between pages; cancel an in-flight scan request when it expires. Preserve verified earlier pages and report the specific stopping reason. Caller cancellation must propagate rather than become a completed scan. Repeated or unsafe continuation links and malformed responses must be rejected using existing Graph safety conventions.

Do not persist user evidence or share snapshots across workspaces or users. A refresh is a read, not an action audit event; retain existing operational logging and safe correlation references. Existing assignment auditing is unchanged.

## Evidence semantics

Available capacity means purchased enabled units not currently consumed according to the existing inventory contract. It does not mean an unused assigned license or guaranteed savings.

A disabled-account finding requires explicit `accountEnabled == false` and valid assigned-license evidence. It means "review this allocation," not "safe to reclaim." Preserve unknown SKU IDs if an assignment cannot be matched to the catalog.

Missing or null account status, missing assignment data, malformed evidence, and unavailable sources never become negative findings or healthy zeros. Distinguish empty valid assignment arrays from absent evidence.

Count evidence-missing records only within the observed scan population. If an authoritative total is unavailable, unscanned users remain an unknown count. Show records assessed, whether scanning completed, and why coverage stopped. Do not claim tenant-wide counts from a bounded or failed scan.

Inactivity and entitlement overlap are unsupported checks in v1. Explain their missing source requirements in the explainer/coverage disclosure; do not render empty fake findings tables or zero-valued KPIs for them.

## Page and filtering

Keep `/licenses` and its existing roster/export behavior. Add a separate License Hygiene route with opt-in access. Use existing Atea tokens and shell; no new logo, typography package, or component framework.

Use `ExplainerPanel` for purpose and limits, `InfoTip` for metric definitions, `KpiFilterTile` for controlled filters, and `DataFreshness` for truthful source status. Callers own loading regions' `aria-busy`; no W0 component edits.

Use separate SKU-capacity and account-review tables. Account rows show identity, disabled status, assigned friendly SKU names and identifiers, and evidence timestamp/source. SKU rows show verified purchased, assigned, and available counts.

Search and SKU selection apply to the same authorized snapshot and recalculate relevant KPI values and both tables. Category tiles select the finding category; show selected state explicitly and make clearing the filter discoverable. Table pagination happens after filtering, not before KPI calculation. Reset pagination when filters change.

Use copy equivalent to "Every KPI and table recalculates when you filter," with definitions clarifying that values cover this snapshot, not unobserved tenant data. Findings are read-only; user-detail links appear only when the existing target route/module permits access.

Red identifies disabled-account review findings. Amber identifies available-capacity review context or incomplete evidence. Green is permitted only for a verified healthy result in a completed relevant assessment; unavailable or partial results never receive a green-zero interpretation. Include labels/icons so color is not the only signal.

Retain original SKU identifiers even when friendly names exist. Do not imply entitlement overlap from similar display names.

## Errors and freshness

Inventory and user-evidence sections degrade independently. A failed SKU source must not erase valid account evidence, and a failed user scan must not make inventory unavailable unnecessarily.

Graph denials, consent problems, throttling, invalid responses, cancellation, and source failures remain explicit. Use repository-standard error categories and actionable UI states; do not swallow errors or return success-shaped empty arrays.

If a later user page fails, preserve only previously verified observations with partial coverage and the actual failure. Never mark the scan complete. If no user page succeeds, show the user source unavailable.

A scan stopped by its budget is partial even if its observed records are fresh. Map backend freshness to W0's supported values explicitly. Old snapshots must not leak across workspace changes; clear or cancel in-flight state. Do not show old rows as current while a replacement request is loading.

## Test requirements

API unit tests cover capability/module activation, unchanged existing grants, authorized and unauthorized reads, Graph final denial, source-independent failure, disabled-account evidence, unknown/missing fields, empty assignments, unknown SKUs, valid continuation handling, repeated/unsafe links, malformed pages, partial later-page errors, cancellation, scan boundaries, and exact missing-evidence counts.

Web unit tests cover existing Licenses behavior, authorized hygiene discovery, route gating, friendly names and original IDs, same-snapshot search/SKU filtering, category toggle/reset, post-filter pagination, source-specific loading/errors, keyboard controls, missing-evidence disclosures, partial coverage, unavailable values, and workspace-switch isolation.

Owned-page E2E tests cover navigation, access denial, finding filters, evidence disclosure, completed-empty versus incomplete-zero states, and responsive/keyboard behavior. Use existing runners and fixtures rather than new tooling.

Verify no write request or reclaim UI is introduced.

## Delivery and approval gates

This design has conversational architecture and behavior approval. Written-spec approval, including the proposed page/time budget, is still required.

After written-spec approval, create the detailed implementation plan using GPT-6 Luna with high reasoning effort. Obtain user plan approval, then create a persistent checkpoint describing the approved plan before execution.

Implementation runs only on GPT-6 Luna at low/medium effort or explicitly allowed Claude Sonnet 5.5 subagents. Keep implementation isolated in this worktree. Rebase on master before opening one PR to master; do not merge or approve cloud deployments.

After the PR is open and CI is green, send its link to the coordinating session. Tenant-dependent validation limits must remain explicit.
