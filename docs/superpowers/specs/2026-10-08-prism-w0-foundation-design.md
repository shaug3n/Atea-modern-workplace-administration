# W0 Foundation design

## Intent and success criteria

W0 establishes the shared UI and registration contracts needed by seven parallel feature branches. It must merge to master before those branches start. The change must remain additive, preserve existing authorization and navigation behavior, and avoid implementing any future feature.

The user approved the additive-contract architecture, the reservation names below, and the component and registration boundaries described here. This written specification still requires user review before detailed implementation planning.

During plan mode this specification stays in session storage. Its eventual repository destination is `docs/superpowers/specs/2026-10-08-prism-w0-foundation-design.md`. No repository changes or commits occur before the implementation plan is approved.

Success means:

- Feature branches can reuse documented UI contracts without creating competing components.
- Future capability and module names are agreed without granting or enabling future features.
- Navigation reads route metadata while preserving current visibility and route behavior.
- Feature-local messages and service registration have small, working examples.
- The sanitized scouting report and module inventory clearly separate observations, planned work, and shipped functionality.

## Scope and exclusions

Included: shared components, their tests and styles, registration contracts, metadata-driven navigation, sanitized report, module inventory, and contributor documentation.

Excluded: F1 Overview redesign, F2 Users/User360, F3 Devices/Device360, F4 Licenses/Hygiene, F5 Passkeys/MFA campaigns, F6 About/Changelog/Feedback, F7 access-transparency features, and W2 sweep. No placeholder pages, new business endpoints, new Graph permissions, default grants, feature activation, or theme replacement.

The existing application remains the source of implementation patterns. Preserve Atea branding, semantic theme tokens, established geometry, and typed localization. Do not introduce a component framework or feature-plugin system.

## Alternatives and selected approach

1. **Selected: additive contracts and metadata-driven navigation.** Implement small shared primitives and typed route metadata now. Demonstrate feature-local messages and service registration using an existing feature. This reduces future conflicts with limited migration.
2. **Documentation-only conventions.** Smaller immediate change, but parallel branches still compete over navigation and registration implementation.
3. **Feature-local route and navigation registry.** Greater isolation, but excessive architectural churn for a prerequisite foundation.

## Shared UI contracts

Extend `MetricCard`, `DataFreshness`, `PermissionState`, `ConfirmationDialog`, and `StatusBadge`; do not fork their behavior or break existing call sites. Add distinct components only for genuinely distinct responsibilities.

| Component | Responsibility and boundary |
|---|---|
| DomainAccessChip | Present an explicitly supplied `CapabilityDecision` with readable state and reason. Read/write labels, where needed, come from separately evaluated decisions. Never infer write access from read access, role names, or module membership. Preserve hidden-state semantics. |
| DisabledReason | Explain disabled interaction through visible or keyboard-accessible text and an accessible tooltip. Support a source-of-truth lock explanation. A disabled native control must not be the only trigger for reading the explanation. This is presentation, not enforcement. |
| KpiFilterTile | Reuse MetricCard's visual vocabulary for a controlled, selectable native button with `aria-pressed`. Keep navigation cards and filter buttons distinct; no nested interactive elements. Existing MetricCard usage stays unchanged. |
| StatusPillFilter | Controlled status-filter buttons with labels, counts, and explicit selection. Do not rely on color alone. Caller owns filtering, selection rules, and data. |
| ExplainerPanel and InfoTip | Reusable explanatory content and labeled, keyboard-accessible help. Tooltip/help content must be available without mouse hover. Escape and focus behavior must match the chosen existing implementation pattern. |
| ProvenanceChip | Show caller-provided source/provenance labels and optional explanation. Do not fetch data or imply that provenance is verified by the component. |
| QuickActionsBar | Arrange supplied action controls with the notice "All actions are audit-logged". Consumers must use actions backed by their feature's audit contract. Rendering the notice does not implement audit logging or justify using it for unaudited actions. |
| DataFreshness | Preserve existing props and default presentation. Add compact pill and banner presentations, including stale, partial, unavailable, and throttled conditions. Treat partial data as independent from freshness. Throttling must not display success or invent retry timing. Keep fetched time, source, supplied messages, and refresh behavior. |
| ReasonDialog | Compose the existing confirmation shell with a required, labeled reason field and audit notice. Trim for blank validation; blank input cannot invoke confirmation. Caller receives the reason and owns submission, server validation, and logging. Preserve busy, cancel, focus-containment, and destructive-confirmation behavior. |
| Skeleton/loading convention | One reusable skeleton convention with accessible loading text/status and decorative placeholders hidden from assistive technology. Caller marks its loading region appropriately. Respect reduced motion; do not add a new data-loading framework. |

Shared primitives consume typed shared messages. Feature-specific copy stays with features. Defaults preserve current behavior; new states and presentations are opt-in. Errors must remain explicit, not success-shaped fallbacks.

## Reserved authorization and module names

| Future feature | Reserved module | Reserved capabilities |
|---|---|---|
| Passkeys/MFA campaigns | `authentication-campaigns` | `authentication.campaigns.view`, `authentication.campaigns.manage` |
| License Hygiene | `license-hygiene` | `licenses.hygiene.view` |
| About, system versions, changelog | `about` | `platform.about.view` |
| Feedback | `feedback` | `feedback.submit` |

Add capability names to the FE type contract in `src/Web/src/capabilities/capabilityTypes.ts` and BE constants in `src/Api/Authorization/Capability.cs`. Separate reservations from active entries in `Capability.All`. Do not change evaluator rules to grant these capabilities.

Represent module reservations explicitly in `WorkspaceModuleCatalog.cs`, separate from `KnownModules` and grantable/default/effective modules. Extend the `AppRoute.module` type for future registration without adding future routes. Reservation alone must not make a key a supported entitlement.

Existing `authentication.methods.view/manage` permissions remain unchanged. Passkeys and MFA share the campaign module. Future branches must deliberately add validated authorization and module activation when their features are ready.

Tests must verify that reserved names cannot produce allowed decisions or effective module access. If current unknown-key behavior complicates this boundary, preserve deny-by-default and document the exact distinction rather than treating reservations as active.

## Route-metadata-driven navigation

Add typed navigation metadata to the existing `AppRoute` contract and consume it in `PrimaryNav`. Keep existing route resolution and rendering architecture.

Navigation groups, in order:

1. Overview
2. Identity & Access
3. Devices
4. Licenses
5. Services
6. Operations
7. Platform

Route metadata distinguishes navigable destinations from detail routes. It provides group and ordering information without generating entries for all routes automatically.

Map existing Overview destinations to Overview, user destinations to Identity & Access, device destinations to Devices, license destinations to Licenses, Exchange/service destinations to Services, audit/activity destinations to Operations, and settings/administration destinations to Platform. Inventory actual destinations during detailed planning and explicitly preserve any route-specific gate or presentation requirement.

Preserve module access, capability decisions, workspace-access gates, audit visibility, settings visibility, active-route matching, labels, and `aria-current`. Metadata must not bypass these checks. Omit empty groups after filtering. No future module gets a navigation entry.

Later branches add their route and navigation metadata in the established route surface. W0 reduces duplicate nav declarations; it does not promise zero textual conflicts in central route composition.

## Feature-local localization

Establish `src/Web/src/features/<feature>/messages.ts` as the feature-copy ownership pattern.

Preserve existing shared messages and their typed imports. Demonstrate the pattern by moving a bounded set of an existing feature's messages without changing rendered copy. Maintain literal-key inference and compile-time key checking. No `any`, string-key escape hatch, or silent missing-message fallback.

Use an explicit composition convention compatible with the current English message object. Feature branches own their feature message files and make only the necessary central composition change. Document naming and collision avoidance; do not create empty message files for future features or build a runtime localization registry.

## Backend feature registration

Introduce a small per-feature `AddXFeature()` extension for service registrations, demonstrated on an existing feature such as Overview.

`Program.cs` gets one service-registration call for that feature. Existing `MapXEndpoints()` calls remain in the post-build application phase. Service registration and endpoint mapping are separate lifecycle operations; do not combine them into a misleading single call.

Keep shared authorization, auditing, security setup, and cross-feature infrastructure centralized. Move only the representative feature's owned service registrations. Do not migrate every feature or create future registration stubs.

Document how future branches add their owned registrations and endpoint mappings. Preserve existing lifetimes and endpoint behavior.

## Report sanitization and documentation

Read the supplied local scouting report only. Do not access `https://prism.orklait.no`.

Create `docs/ux-inspection/prism-scouting-report.md` from the report after removing personal names, email addresses, user/device identifiers, tenant identifiers, and identifying sample values. Refer to sample subjects as "test user" and "test device"; generalize other sensitive examples while retaining the UX finding.

Do not copy identifying screenshots, URLs with identifying parameters, hidden metadata, or secrets into the repository. Preserve useful observed behavior and clearly label it as scouting, not proof of current application implementation. Review the resulting document before staging or committing.

Update `docs/module-inventory.md` to mark Passkeys/MFA, License Hygiene, and About/Feedback as planned wave 1. Planned does not mean shipped, enabled, validated, or assigned permissions. Preserve unknown source/scope limitations.

Document component APIs, route metadata, inactive reservations, feature message ownership, service-registration lifecycle, and later-branch activation responsibilities in an appropriate contributor-facing repository document.

## Validation and acceptance

Use existing Vitest/web unit tooling and API unit tooling. Detailed planning must confirm exact scripts and test selectors before execution.

- Existing component call sites build without changes unless an intentional, bounded migration is documented.
- Primitive tests cover button semantics, selection, counts, accessible reasons/help, hidden authorization decisions, freshness combinations, throttling, loading accessibility, and required-reason behavior.
- Navigation tests compare current gating and active-route behavior against the metadata-based implementation, including hidden destinations and empty groups.
- Contract tests establish FE/BE key agreement and inactive capability/module reservations without changed default grants.
- The registration example preserves dependency lifetimes and existing endpoint behavior.
- Localization preserves rendered English text and compile-time key inference.
- Build/type-check and targeted API tests cover changed surfaces.
- Review report sanitization directly; generic keyword scanning alone does not establish anonymity.

## Execution constraints

After written-spec approval, detailed planning uses GPT-6 Luna with high reasoning effort. Implementation uses GPT-6 Luna with low or medium reasoning effort, or Claude Sonnet 5.5 only if explicitly selected.

Before executing the approved implementation plan, create a persistent checkpoint summarizing the approved scope, current branch state, and rollback reference. Do not include unrelated changes or secrets.

Open a PR after verified implementation. Report its link to the coordinator when CI is green. W0 must merge to master before feature branch work begins; do not start those branches from this unmerged foundation.
