# F3: Devices directory and Device 360

## Intent and agreed scope

Help workplace administrators understand a device's reported condition, inspect supporting evidence, and choose an existing management action without confusing assignment intent, request acceptance, or missing telemetry with effective state.

The user approved an architectural extension of the existing Devices feature, not a replacement platform. W0 PR #10 is merged into this worktree's base. Reuse W0 primitives, existing application tokens, routes, localization, authorization, PIM guidance, auditing, and recovery controls.

Owned implementation surfaces are `src/Web/src/features/devices/**`, `src/Api/Features/Devices/**`, and their tests. Shared changes must be minimal and additive under `docs/contributing/feature-extension-contracts.md`. Do not edit W0 primitives in `src/Web/src/components/`; request coordinator help if a primitive is insufficient.

Excluded: other feature branches, warranty, patch management, local-admin grants, new management commands, a general assignment-applicability engine, cloud deployments, and merging. Do not access `prism.orklait.no`.

## Approved decisions

1. Use feature-owned lazy tab endpoints rather than one expanded aggregate response or a new generic aggregation layer.
2. The domain chip displays the primary user's UPN suffix, labelled **User domain**. It is not the device's joined domain. Missing or malformed UPNs produce an explicit unavailable value; never infer from a hostname.
3. Configuration defaults to **Reported by device**. **From assignment** is a separate assignment-target view, not proof of effective configuration.
4. Configuration status counts cover all loaded records in that view, not the current table page. Partial collections retain an explicit partial-coverage indication.
5. Beta Graph reads and writes are permitted by the user, but F3 adds read operations only. Existing write actions and gates remain unchanged.
6. Assignment targets are limited to policies identified in device reports. The view does not resolve every group, filter, exclusion, Settings Catalog policy, or device applicability.
7. No tenant-wide app scan to reconstruct per-device inventory. Use the documented per-device relationship.

## Architecture and integration

Keep the core detail request responsible for device identity and persistent header information. Add compatible fields for user display name, UPN, and nullable reported encryption where available from the managed-device response. Do not make optional tab permissions prerequisites for core reads.

Feature-owned read endpoints and services supply per-policy compliance, configuration state/assignment targets, discovered apps, and protection details. Follow existing feature registration, Graph-client, response/error, and capability-evaluation patterns. Additive selection/mapping changes to the existing managed-device Graph reader are permitted when necessary; put new tab orchestration and contracts inside the Devices feature.

Each endpoint enforces authentication, the devices module gate, and the appropriate existing read capability server-side. OAuth scopes and Intune role authorization remain separate requirements. New optional scopes must be represented in `GraphScopeCatalog` and `docs/module-inventory.md` with the least privilege and independently evaluated access.

Each tab owns its loading, errors, retry, source freshness, and partial coverage. Load data only when needed. Reuse successful data using existing query conventions; guard against late responses after a device or tab changes. Ordinary detail loads must not trigger recovery-secret retrieval. Never cache secret values in tab data.

Keep current device-route ID semantics. Do not interchange Intune managed-device IDs, Entra device IDs, and Entra object IDs. User links continue to use the existing user detail route and its own access enforcement.

## Directory

Preserve search, compliance and OS filters, pagination, result counts, export, selection, and existing actions.

Replace applicable noninteractive summary cards with controlled `KpiFilterTile` controls. Selecting a tile applies the corresponding existing filter; selecting it again clears that tile's filter. Keep tiles and conventional filters synchronized, reset pagination when filters change, and preserve unrelated filters.

Counts must describe their actual data scope. Do not represent a loaded-page count as a tenant-wide total. Retain server summary counts where available; label partial coverage explicitly. Keyboard interaction and selected state must be accessible, and zero-result states must permit clearing filters.

## Device 360 header

Keep identity and back navigation persistent above Overview, Configuration, Apps, Security, and Actions.

Show compliance, activity, and ownership as labelled status pills. Ownership maps reported company/personal/unknown values without inventing a value. Activity comes from available source telemetry; its help text names each available source and timestamp/age. Do not treat a recent API fetch as a recent device check-in. Missing timestamps stay unknown; preserve existing freshness conventions rather than introducing an arbitrary health claim.

Show User domain, primary-user display name with a user link when an ID exists, and last check-in. Use reported names rather than raw IDs when available. An absent user association is distinct from an unreadable user attribute.

## Overview

Present Essentials with `ProvenanceChip` naming actual source attributes, including `managedDevice.deviceName`, `managedDevice.managedDeviceOwnerType`, `managedDevice.userDisplayName`, `managedDevice.userPrincipalName`, and `managedDevice.lastSyncDateTime`.

Preserve useful hardware/management information and technical-ID access. Use `InfoTip` to explain that overall Intune compliance and individual policy reports can disagree or update at different times. Do not recompute the authoritative overall state by reducing the policy table. Lazy per-policy reporting has its own loading/failure state.

## Configuration

Use device-reported legacy configuration states as the default evidence. Show readable names and reported states, with `StatusPillFilter` counts over the loaded collection. Preserve unknown or future states rather than mapping them to success. Provide a clearly labelled technical-ID toggle.

The assignment view retrieves assignment targets for policies identified in device reports. Distinguish inclusion, exclusion, and filter metadata when returned. Do not claim a target currently includes this device or infer group applicability. An empty reported-policy collection is not evidence that no policies are assigned.

Clearly state that this bounded view is not complete Settings Catalog coverage and that Intune does not expose every effective per-device setting through these sources. Do not substitute a healthy-looking assignment table for unavailable reported state.

Provide an Intune link. A device-specific link must use a verified, encoded route and be treated as best-effort because no stable Microsoft portal-link contract was established. If it cannot be verified, provide an accurately labelled Intune portal entry point and a copyable managed-device ID, not a misleading device link.

## Apps

Use the managed-device `detectedApps` relationship, preferring v1.0 if its relationship is verified there, otherwise the documented beta relationship. Follow collection pagination; do not silently drop later pages.

Display discovered app name, version, platform, and publisher when meaningful. A missing publisher or certificate-style distinguished name must not appear as a friendly publisher. Use explicit unavailable/unknown copy without fabricating a company name. Keep normalization conservative and tested.

Describe the list as discovered inventory, not a complete inventory or assignment/install-success report. Preserve source and coverage caveats, including ownership/platform limitations.

## Security

Keep LAPS and BitLocker metadata/reveal behavior intact within the Security tab: independent capabilities, required reason, auditing, no-store, automatic clearing, stale-response protection, and existing platform/target checks.

Show nullable device-reported encryption separately from BitLocker recovery availability. Missing Boolean data must not become `false`, and the reported encryption flag does not prove a specific encryption technology or current recovery-key availability.

Use `windowsProtectionState` for read-only Windows protection details where supported, retaining its reported timestamp. Do not describe `partnerReportedThreatState` as Defender Antivirus telemetry. Non-Windows, unsupported, and denied states remain distinct from healthy or empty states.

## Actions

Retain only the repository's existing commands. Preserve capability, module, PIM, target/platform, confirmation, idempotency, and audit enforcement.

Add a **Choose the right action** decision table covering what survives, what is destroyed or removed, whether re-enrollment is needed, and when to use each supported command. Describe platform-dependent consequences explicitly; do not make universal promises from one platform's behavior.

Show **Acceptance does not confirm completion** before destructive confirmation and in request-result feedback. Keep request acceptance distinct from device execution. No new polling/completion claim is introduced without existing evidence.

## Data and error contract

Use repository-standard response contracts extended additively for source/freshness and independent section status as needed. Keep empty successful collections distinct from unsupported, access-denied, missing consent, missing device, transient failure, and partial reads.

Never catch all Graph errors and return success-shaped empty data. Preserve standard logging and actionable access/error guidance. Restrict retries and pagination to existing transport conventions; honor cancellation and throttling.

Source timestamps and API retrieval timestamps are separate. Display data with its actual provenance. Do not manufacture a source timestamp from the fetch time.

Use existing device read scopes for fields and routes that support them. Add `DeviceManagementConfiguration.Read.All` only where the selected configuration/assignment operation requires it. No additional user scope is needed for name and UPN already returned by `managedDevice`. Do not add new write scopes merely because beta writes are permitted.

## Evidence and implementation constraints

Microsoft documents the beta managed-device relationships `detectedApps` and `windowsProtectionState`. The first research pass did not verify the beta relationship and must not be used to justify a tenant-wide app scan.

The v1.0 SDK documents per-device compliance-policy and configuration-state commands. REST operation names, selected fields, version, and permissions must be checked against the actual SDK/Graph metadata when implementing. Keep version choices explicit; do not hide route incompatibility behind a fabricated empty result.

Primary sources:

- Managed-device fields and beta relationships: https://learn.microsoft.com/en-us/graph/api/resources/intune-devices-manageddevice?view=graph-rest-beta
- Managed-device read permission: https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-get?view=graph-rest-1.0
- Detected-app collection and permission: https://learn.microsoft.com/en-us/graph/api/intune-devices-detectedapp-list?view=graph-rest-beta
- Protection-state fields and permission alternatives: https://learn.microsoft.com/en-us/graph/api/intune-devices-windowsprotectionstate-get?view=graph-rest-beta
- Per-device compliance policy state command: https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.devicemanagement/get-mgdevicemanagementmanageddevicecompliancepolicystate?view=graph-powershell-1.0
- Per-device configuration state command: https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.devicemanagement/get-mgdevicemanagementmanageddeviceconfigurationstate?view=graph-powershell-1.0
- Legacy configuration assignments: https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-deviceconfigurationassignment-list?view=graph-rest-1.0

No live tenant validation was performed during design. Mocked tests alone must not be described as proof of tenant permissions, effective state, or portal-link reliability.

## Verification and acceptance

Web unit tests cover direct-route header loading, user/domain fallbacks, provenance, overall-versus-policy help, tab lazy loading, local skeleton/error/retry states, stale-response handling, filter synchronization, full-loaded-collection status counts, technical-ID toggling, assignment coverage warnings, pagination, and publisher normalization.

Preserve and extend recovery tests for reason validation, access/PIM handling, automatic secret clearing, and stale responses. Test leaving the Security tab and changing devices so sensitive values do not persist unexpectedly. Action tests preserve confirmations and prove acceptance feedback never claims completion.

API unit tests cover route-specific scope requests, nullable mapping, unknown enums, device-ID usage, paginated collections, partial/denied/unsupported/transient responses, and unchanged module/capability/recovery/action gates. Test source timestamps separately from fetch timestamps.

Owned-page E2E tests cover directory tile/filter behavior and Device 360 navigation, lazy request boundaries, responsive layout, keyboard operation, and representative denied/partial states. Use existing test infrastructure; do not issue destructive commands to a real tenant.

Use existing Atea tokens and components. Verify desktop/narrow layouts, semantic tab controls, keyboard focus, readable status text, tooltips available to keyboard users, region `aria-busy`, and distinct loading/empty/error states.

Run the smallest relevant Web/API test selectors first, then the Web build/type check and owned-page E2E. Rebase on master before opening one PR to master. Do not merge or approve cloud deployments. Send the coordinator the PR link only after its required CI is green.

## Handoff and approval sequence

This is the written-spec draft, not execution approval. During plan mode it stays in session artifacts. After execution approval, persist the approved specification at `docs/superpowers/specs/2026-10-08-f3-device-360-design.md`.

After the user approves this written specification, create the detailed implementation plan using GPT-6 Luna with high reasoning effort and save it as the session's `plan.md`. Ask the user to approve that plan and execution method.

Before execution, create a persistent checkpoint summarizing the approved plan and repository state. Implementation uses GPT-6 Luna with low/medium reasoning effort, or Claude Sonnet 5.5 only when explicitly selected. The current design stage uses GPT-6.1 Sol.
