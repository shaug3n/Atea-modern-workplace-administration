# Workspace UX redesign

**Date:** 2026-09-25
**Status:** Design agreed in conversation; written spec awaiting review
**Scope:** Customer workspace UI and the license label returned by its existing API. The separate Atea platform administration console is outside this redesign.

## Outcome and design priority

Customer IT administrators need to find a task, understand the available data, and act without decoding crowded layouts or Microsoft product codes. Success means that Users, Devices, Licenses, Exchange, Activity, and Workspace Settings share a clear visual hierarchy; common actions are easy to find; data views remain useful at narrow widths; and authorization problems no longer dominate page layouts. Atea identity remains recognizable through the existing logo and restrained brand accents, but accessibility and task clarity take priority over brand recipes. Light remains the default theme and dark mode remains available.

The existing separation of workspace module access and Microsoft delegated authorization stays intact. A workspace role never grants an Entra or Intune privilege. The API continues to enforce the signed-in user's delegated Graph scopes, Entra RBAC, and active PIM state for every operation.

## Chosen approach

Build a small shared workspace UX layer in the current React app: shell navigation, notification aggregation, page headers and actions, data-view primitives, and explicit load/empty/unavailable states. Adapt each existing operational page to those primitives. Keep the existing routes, APIs, and authorization boundaries unless a change below is specified.

Page-specific CSS patches would leave different interaction patterns across modules. Replacing the whole component stack would add migration work without addressing the underlying information hierarchy. The shared layer is deliberately small: components own presentation and interaction, while feature pages retain their data loading and domain actions.

## Navigation and shell

- The main customer navigation contains Overview, People, Devices, Services, Activity, and one **Workspace Settings** destination. People contains Users and Licenses; Services contains Exchange. The grouped headings act as disclosure buttons with clear expanded state. Subitems use smaller type and indentation; the active page has an explicit label and visual treatment. Only assigned and enabled workspace modules appear. A setup administrator retains the existing Devices setup path.
- A module does not disappear merely because its Graph capability check failed. Opening it shows its ordinary page frame and a concise unavailable state in the data area, with the resolution in Notifications. Unassigned or disabled modules remain inaccessible, including by direct URL. Server-side module guards remain authoritative.
- The header has workspace context, a labelled **Notifications** control, theme toggle, and signed-in user. On narrow screens the navigation uses an accessible menu. Breadcrumbs, where useful, reflect actual destinations rather than redundant hierarchy.
- Spacing, type sizes, control heights, and content widths use a limited semantic scale rather than page-specific positional rules. Primary actions sit beside the page title or at the start of the relevant section. Search and filters sit immediately above results. Row actions sit beside their row or in a labelled menu. Destructive actions remain in explicit confirmation flows.
- Keep the original Atea artwork and existing light/dark semantic tokens. Layout, contrast, focus visibility, and readable type are allowed to depart from the Atea design guide where needed.

## Notifications and authorization states

One header dropdown collects workspace connection and consent problems, missing Graph scopes, personal Entra RBAC/PIM limitations, and temporary data failures. It consumes the existing capability snapshot, connection-health response, and safely classified page request failures. A frontend notification controller normalizes these into issue records with an ID, affected area, cause, severity, audience, next action, last checked time, and optional correlation ID. It is scoped to the current workspace and session and does not persist sensitive Graph responses or user data.

The dropdown groups related decisions by root cause and affected module. A Global Reader's expected inability to write appears under **Your access** without producing one alert per button or increasing the warning count. The count reflects unresolved actionable issues or failures, not every read-only capability. Missing consent and missing registration scope must be distinguished only when the existing evidence supports it; otherwise the wording says verification is unavailable. An issue clears after a successful recheck, a refreshed capability snapshot, or a successful retry. The control includes a manual refresh and does not repeatedly announce unchanged issues.

Every issue describes what is affected and an action the current user can take. Users with workspace settings access may open the relevant section of Workspace Settings. Other users see guidance to contact a workspace administrator. Eligible PIM users receive the existing activation or guided handoff link. Notification links never bypass route or API authorization. The dropdown has a visible text label, keyboard operation, correct focus handling, Escape dismissal, and readable state and count announcements.

Operational pages retain their normal title, filters, and data region when Graph is unavailable. The affected region says briefly that data cannot be shown and offers retry when meaningful; diagnosis and resolution are in Notifications. A successful partial response continues to show verified data with a modest freshness or partial-data indicator. The UI never substitutes zero or “no results” for a failed request. If an entire view is unauthorized, no protected data is requested or rendered. `read_only` view decisions permit viewing while mutation actions remain disabled or absent. The API remains the final authority if frontend state is stale.

## Operational pages and tables

Users, Devices, Licenses, Exchange, and Activity use the same page anatomy: title and primary action, optional summary, search/filter bar, results, then pagination or continuation. Primary columns answer the page's main task; less-used fields live in an expandable row detail or the existing detail page. Numeric columns align consistently and use tabular figures. Tables use quiet dividers, readable row spacing, meaningful headers, explicit sort direction where sorting exists, and stable loading/empty states. Actions have names and keyboard targets; the first useful action is not hidden at the far right of an oversized table.

At narrow widths, results become a deliberate summary-and-details list using the same data and actions, rather than forcing the whole page to scroll sideways. There must be no page-level horizontal overflow at common mobile widths or 200% zoom. Large desktop tables can still use the available content width. Each module retains its supported features and safety confirmations; this redesign does not create new Microsoft 365 operations.

## License names

The Graph subscribed-SKU response currently supplies `skuPartNumber`, and the API repeats it as `displayName`. Add a versioned, locally maintained mapping from known SKU part numbers to familiar English product names. The license API continues returning both `displayName` and `partNumber`, so existing clients retain their contract. The user-facing name is primary; the exact part number and SKU ID are available as secondary identifiers. Search matches both friendly name and part number.

Cover the SKUs seen in the test tenant and a documented set of common Microsoft 365 offerings with explicit mapping tests. Unknown codes remain visible as codes with a clear “Product name unavailable” label; do not fabricate a product name from a code. The mapping can be updated independently of the page layout and requires no tenant setting or database migration.

## Workspace Settings

Replace the settings landing page and separate customer settings navigation entries with one `/settings` page. It has a compact in-page index and four stacked, clearly headed sections: Connection, General, Modules, and Access. Each section loads and saves through its existing endpoint and fails independently; one unavailable section does not blank the others. Render only sections the session's workspace permissions allow. Users who may manage members but not general settings can still reach Access.

Keep the old `/settings/setup`, `/settings/general`, `/settings/modules`, `/settings/access`, `/workspace-settings`, `/workspace-access`, and `/onboarding` paths as redirects or deep links to the corresponding section, preserving direct links and consent callbacks. A notification link to a section must land with the section heading visible and focused appropriately. The connection section retains the distinction between adding delegated scopes to Atea's API registration and granting customer-tenant consent.

## Error handling and data flow

The existing session endpoint determines workspace membership, enabled modules, and settings permissions. Capability and connection checks load separately. Feature pages keep their existing data endpoints and report only a sanitized issue category, affected module, retry action, and safe correlation reference to the notification controller. The controller deduplicates reports and discards them when the workspace/session changes. It does not store token content, secrets, mailbox data, recovery values, or raw Graph error bodies.

Authentication failure, missing workspace membership, and disabled module access remain distinct full-page states because the normal workspace cannot safely function in those cases. Graph consent, user role/PIM limitations, and transient Graph errors use the stable workspace shell and notification model. During unknown authorization state, mutations are unavailable until a fresh allowed decision is present. Refresh after consent/PIM return or explicit retry; never infer that completing a guide step granted access before the API confirms it.

## Verification

- Component tests cover disclosure navigation, active state, settings deep links, notification grouping/count/actions, keyboard behavior, and stale-to-refreshed issue transitions.
- Page tests cover common table states, accessible actions, partial responses, unavailable Graph checks, reader access, PIM guidance, and settings-section isolation.
- API/unit tests cover mapped and unknown SKU labels, searching by name or part number, and unchanged license response fields. Existing authorization and module-guard tests must continue to pass.
- Run the frontend build and relevant .NET tests. Visually inspect desktop, narrow screen, 200% zoom, light and dark mode, keyboard navigation, and long values in the locally running app. Test with a workspace admin and a lower-privilege reader when the signed-in test tenant is available. Record any browser or tenant-access limitation rather than claiming it was checked.

## Non-goals

No new Graph permission, new Microsoft 365 management action, database migration, separate Atea platform-console redesign, or persisted notification inbox is required. This work does not turn workspace roles into tenant privileges or hide unresolved errors behind empty data.
