# Authentication campaigns: F5 design

## Status and intent

The user approved the architecture, reporting semantics, and UX/error-handling sections on 8 October 2026. This written specification still requires user review before implementation planning.

This file is staged outside the repository because the session is in plan mode. After execution approval, publish it as `docs/superpowers/specs/2026-10-08-authentication-campaigns-design.md`. Do not treat conversational design approval as implementation approval.

Administrators need to understand passkey registration and identify accounts worth reviewing during an SMS/Phone MFA transition. The module must explain its terms, distinguish observations from assumptions, and remain useful when reporting or directory enrichment is incomplete.

Success means an authorized administrator can inspect actual Graph-backed registration facts, filter accounts, and compare organizational groups without interpreting registration as sign-in usage, policy eligibility, or a completed migration.

## Confirmed scope

- Read-only Graph reporting with Passkeys and SMS/Phone views.
- Registration-first reporting; policy eligibility is explicitly not evaluated.
- Default population: member accounts present in the registration report. Guests are selectable separately.
- Department, office location, and company rollups. Manager rollups are deferred.
- No deadline configuration in v1. Display `Deadline not configured`.
- Default SMS/Phone candidates: accounts with reported SMS/voice preferred methods. A separate filter shows all phone-registered accounts.
- Explainer-first copy, filterable KPIs, collapsible rollups, searchable paginated account tables, and transparent freshness/coverage.

No policy writes, authentication-method changes, bulk enforcement, notifications, campaign-setting writes, PIM activation, sign-in usage analytics, historical snapshots, or migration-history claims. No access to `prism.orklait.no`.

## Architecture and alternatives

Use one `authentication-campaigns` module with two reporting views and a shared registration-report reader. Join registration records to directory attributes by user object ID. Keep normalization, aggregation, and presentation inside owned feature boundaries.

Separate readers per view would duplicate Graph loads and risk inconsistent definitions. Persistent campaign snapshots would enable history, but introduce storage, retention, and operational scope not requested for v1. Neither alternative is selected.

Owned implementation roots:

- `src/Api/Features/AuthenticationCampaigns/**`
- `src/Web/src/features/authentication-campaigns/**`
- Corresponding tests under `tests/Api.UnitTests`, `tests/Web.UnitTests`, and `tests/Web.E2E`.

Follow `docs/contributing/feature-extension-contracts.md`. Shared registration changes must be minimal and additive. Do not modify W0 primitives under `src/Web/src/components/`. Escalate any missing primitive contract to the coordinator.

Follow existing feature service-registration and endpoint-mapping conventions. Frontend route metadata is not API authorization. The API must enforce the workspace module and campaign view capability before reading Graph or returning campaign data.

Activate module `authentication-campaigns` and capability `authentication.campaigns.view` deliberately. Leave `authentication.campaigns.manage` reserved and denied. Adjust reserved-contract tests only to reflect this intentional view activation; preserve deny-by-default behavior for management.

## Sources and authorization

Primary source:

`GET /reports/authenticationMethods/userRegistrationDetails`

Microsoft Graph v1.0 documents `AuditLog.Read.All` as the least-privileged delegated and application permission. Its endpoint-specific delegated role list includes Reports Reader, Security Reader, Security Administrator, and Global Reader. Use the endpoint-specific requirements, rather than inferring access from broader Entra portal roles or existing per-user authentication-method permissions.

Use the application's existing delegated authentication and active-role/PIM evidence patterns. Eligible-but-inactive role membership must not be represented as active access. Do not broaden campaign access using the existing authentication-method management gate.

The report resource links to Authentication Methods Activity licensing requirements. Microsoft's guidance requires Entra ID P1 or P2 for Usage and insights. Document this prerequisite without claiming the application can determine tenant licensing from an unrelated field.

Directory enrichment uses existing authorized directory-read patterns for `department`, `officeLocation`, and `companyName`. Detailed planning must identify the exact endpoint selection and least-privileged scope already supported by the repository. Reuse existing directory reads where possible; do not add per-account authentication-method calls or manager calls.

The authentication-method policy endpoint is intentionally not required in this design. No policy read scope should be requested solely for eligibility that v1 does not calculate.

Document new report requirements and module status in `GraphScopeCatalog` and `docs/module-inventory.md`. Preserve established error translation, logging, and audit conventions. There are no mutation audit events because there are no mutation endpoints.

## Data and metric semantics

### Population and completeness

The report does not include disabled or recently deleted accounts. Report population is not the full tenant directory. Member and guest filters operate on the reported `userType`; unfamiliar or missing values must not silently become members.

Follow Graph pagination and honor cancellation and repository-standard retry/error behavior. Deduplicate by object ID before aggregation. When the source is incomplete, expose coverage and observed counts. Never extrapolate observed records into tenant-wide totals.

Every metric and percentage must identify its population and denominator. Missing records or fields are unknown, not negative. A zero denominator produces a not-applicable state, not a fabricated percentage.

### Passkeys

Count explicit passkey registration tokens documented for the report, including `passKeyDeviceBound`. Generic FIDO2 registrations form a separate, clearly labeled category. Do not count Microsoft Authenticator passwordless, Windows Hello for Business, or `isPasswordlessCapable` as passkey registrations.

Normalization must preserve unfamiliar method tokens rather than silently treating them as no registration. Tests cover documented tokens, generic FIDO2, mixed registrations, empty collections, and missing collections.

Use registration-oriented copy even if the navigation label says Passkeys adoption. Explain that registration does not establish actual sign-in use.

Eligibility displays `Not evaluated`. Do not show a numeric eligible-account count or infer eligibility from MFA/passwordless capability.

### SMS/Phone

When `isSystemPreferredAuthenticationMethodEnabled` is true, classify candidates using reported `systemPreferredAuthenticationMethods`. When it is false, use `userPreferredMethodForSecondaryAuthentication`. Missing or unknown control/preference values remain unknown; do not silently fall back to another source.

The documented phone preference values include `sms`, `voiceMobile`, `voiceAlternateMobile`, and `voiceOffice`. A system-preferred collection containing a phone method qualifies as a reported phone-preference candidate; it does not establish exclusive phone dependence.

All phone-registered accounts are a separate selectable population. Registration tokens must be mapped using documented report values. A registered phone is not proof of SMS/voice MFA usage.

Show relevant reported registration and MFA-capability facts without asserting that a replacement method satisfies a particular tenant policy. No account is labeled migrated, safe to enforce, or migration complete.

The hero card displays `Deadline not configured`. Its primary CTA navigates to candidate review. Any progress-style visualization represents a labeled current-snapshot coverage ratio, not historical campaign progress.

### Rollups

Group observed records by department, office location, or company. Missing directory values and failed joins remain explicit unknown/unavailable buckets. Show enrichment coverage separately from report coverage.

Enrichment failure must not discard valid registration records or imply that their organizational attributes are empty. Aggregate rollups and account filters use the same normalized dataset and population rules.

## Freshness and error behavior

Return fetch time separately from source `lastUpdatedDateTime`. A fetch timestamp does not make the source report real-time. Microsoft's guidance says most registration reports update within 36 hours, with occasional longer delays.

If report update times vary, expose their range or a conservative aggregate and explain its meaning. Missing timestamps remain unknown.

Distinguish loading, no reported accounts, no matching results, access denied, source unavailable, stale data, and partial results. Preserve valid data when an optional enrichment source fails, but surface an actionable warning through repository-standard state components and logging.

If primary report loading fails before any usable records exist, show unavailable or access-denied state rather than successful zero KPIs. If later paging fails, any displayed results are visibly partial, and metrics remain labeled observed.

Access diagnostics describe the required Graph permission, active supported role, and licensing prerequisite where relevant. Do not auto-activate PIM or claim an access diagnosis more specific than available evidence.

Existing tenant isolation and authorization apply to any reused caching or query mechanisms. Do not introduce cross-tenant snapshots.

## UX and accessibility

Reuse the current app shell, local Atea assets, design tokens, and component conventions. No new branding system or framework.

The module presents two views:

1. Passkeys: definition, registration KPIs, eligibility-not-evaluated explanation, account list, and organizational rollups.
2. SMS/Phone: transition explanation, deadline-not-configured hero, candidate-review CTA, preference/registration filters, account list, and organizational rollups.

Use available W0 primitives such as `KpiFilterTile`, `DataFreshness`, `WorkspaceDataState`, and `ResponsiveDataView` only where their existing APIs fit. Feature-local composition may supply collapsible sections without changing shared primitives.

Controls require persistent accessible labels, visible keyboard focus, semantic headings and tables, and non-color-only status labels. Narrow layouts must preserve all relevant facts and actions. Rollup expansion and KPI filtering must work by keyboard.

Tables show identity and relevant reporting facts, search, result counts, and pagination. Links into existing user detail flows must respect existing user-module access; F5 does not own or modify the per-user authentication-method section.

## Verification

API unit tests cover source paging, deduplication, member/guest handling, token normalization, preference precedence, unknown values, denominators, joins, rollups, report/enrichment failures, freshness, authorization, and management denial.

Web unit tests cover explainer copy, KPI filters, candidate defaults, guest filtering, deadline absence, collapsible rollups, account search/pagination, partial/unavailable/stale states, and access diagnostics.

Owned-page E2E tests cover module navigation, capability gating, both reporting views, read-only interactions, responsive behavior, and representative source/error states using existing repository tooling.

Detailed planning must identify exact targeted test commands and additive integration sites from current code. Execution must verify observable behavior and ensure no Graph mutation paths are introduced.

## Approval and delivery boundaries

After written-spec approval, GPT-6 Luna with high reasoning effort writes the detailed implementation plan. The plan remains in the session's `plan.md` and requires user execution approval.

Before execution, create a checkpoint summarizing the approved plan. Implementation must run on GPT-6 Luna with low/medium reasoning effort or explicitly permitted Claude Sonnet 5.5 agents.

Rebase on `master` before opening one PR targeting `master`. Do not approve cloud deployments or merge. Send the PR link to the coordinator only when the PR is open and CI is green.

## Sources

- Repository inspection: W0 merge `8f50646`; F5 reservations and feature-extension contracts.
- https://learn.microsoft.com/en-us/graph/api/authenticationmethodsroot-list-userregistrationdetails?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/graph/api/resources/userregistrationdetails?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/entra/identity/authentication/howto-authentication-methods-activity
- https://learn.microsoft.com/en-us/graph/api/resources/authenticationmethodspolicy?view=graph-rest-1.0
