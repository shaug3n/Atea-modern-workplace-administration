# Visual UX Inspection Report — Atea Unified Workplace

## 1. Executive summary

The application has a credible enterprise shell and a generally consistent component language, but it currently exposes too much implementation detail and too many permission failures to feel production-ready. The most serious issues are the absence of clear recovery paths for unavailable data, raw Microsoft Graph capability/scope/identifier jargon, dangerous device actions presented without visible safety framing, inconsistent loading states, and settings that require scanning a very long page. Mobile navigation works, but the header consumes a large portion of the viewport and several data-heavy experiences are difficult to scan.

**Top 10 issues:**
1. Unavailable data is surfaced as fragmented warnings with no single actionable recovery path (UX-003).
2. Technical identifiers, capability names, scopes, GUIDs and JSON metadata are exposed as primary content (UX-006, UX-020, UX-023, UX-027, UX-034, UX-049).
3. Device remote actions are presented without visible safety framing or policy context (UX-029).
4. Settings is one extremely long, scan-heavy page with weak section orientation (UX-037).
5. User details can render “Partial user details” while most sections look complete, making completeness unclear (UX-018).
6. License inventory includes unavailable product names, technical SKU labels and GUIDs (UX-023).
7. Disabled Exchange remains directly reachable but offers no direct recovery action (UX-032, UX-033).
8. Settings module switches do not summarize impact before a save (UX-042).
9. Users overflows horizontally at simulated 200% zoom (UX-054).
10. The product has a persistent 401 favicon error on every inspected route (UX-002).

**Maturity score (1–5):** Visual 3.0 · Usability 2.5 · Consistency 3.0 · Accessibility 3.0 · Responsive 2.5

## 2. Environment

- **Date:** 2026-10-07
- **URL:** https://atea-workplace-dev.thankfulbay-7a5080b4.norwayeast.azurecontainerapps.io/
- **Role/workspace:** Signed-in workspace administrator; customer workspace context (PII omitted)
- **Visible modules:** Overview, Users, Licenses, Devices, Activity, Workspace Settings; Exchange is disabled
- **Viewports:** desktop 1440×900, laptop 1280×720, tablet 820×1180, mobile 390×844
- **Themes:** Light and dark; restored to Light at the end
- **Limitations:** Read-only inspection only. No form or destructive action was submitted. Platform admin was reachable in the existing session, so its visible state was inspected without opening creation flows.

## 3. Global/shell findings

### UX-001 — Header metadata competes with task content
- **Area:** Global header
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** Medium
- **Category:** Visual
- **Evidence:** [overview desktop light](screenshots/01-overview-desktop-light.png), [overview desktop dark](screenshots/02-overview-desktop-dark.png)
- **Observed:** Workspace, access snapshot, consent status, signed-in user, notifications and theme control all share one dense horizontal band. Status copy is visually similar to primary navigation and page content.
- **Expected / why it matters:** A professional admin shell should establish page identity and primary actions first; secondary connection telemetry should be quieter or progressively disclosed.
- **Suggested direction:** Reduce header metadata to compact status indicators with clear hierarchy and move detail into notifications/settings.

### UX-002 — Favicon request fails with 401 on every route
- **Area:** Global document chrome
- **Viewport/theme:** all inspected pages
- **Severity:** Low
- **Category:** Bug
- **Evidence:** [overview desktop light](screenshots/01-overview-desktop-light.png) (+ console), [users desktop dark](screenshots/03-users-desktop-dark.png) (+ console)
- **Observed:** Playwright console repeatedly reports `GET /favicon.ico → 401` while page APIs return 200.
- **Expected / why it matters:** A signed-in app should serve its favicon without authentication errors; repeated console errors undermine confidence and can mask real failures.
- **Suggested direction:** Serve the favicon as a public static asset or remove the request path.

### UX-003 — Permission failures are fragmented across the shell and page
- **Area:** Global notifications and data pages
- **Viewport/theme:** desktop-light
- **Severity:** High
- **Category:** Feedback/States
- **Evidence:** [notifications](screenshots/01-overview-desktop-light.png), [user detail](screenshots/05-user-detail-desktop-light.png), [devices](screenshots/12-devices-desktop-light.png)
- **Observed:** The shell reports warning counts, individual pages show “Partial user details”, “Directory data is unavailable”, stale/fresh labels and disabled controls, but there is no consolidated explanation of what is unavailable and how to remediate it.
- **Expected / why it matters:** Administrators need one source of truth for access blockers; scattered warnings force cross-page diagnosis.
- **Suggested direction:** Provide a consistent permission-health panel with affected features, required role/consent in plain language, and a single setup link.

### UX-004 — Status vocabulary is inconsistent
- **Area:** Global status messaging
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** Medium
- **Category:** Copy
- **Evidence:** [overview](screenshots/01-overview-desktop-light.png), [licenses](screenshots/11-licenses-desktop-light.png), [devices](screenshots/12-devices-desktop-light.png)
- **Observed:** The app alternates among “fresh”, “live”, “cached”, “stale”, “unavailable”, “checking access…” and “loading managed devices…” without a visible shared legend or consistent visual treatment.
- **Expected / why it matters:** Users should be able to interpret data confidence at a glance, especially in an admin product.
- **Suggested direction:** Define a small status taxonomy and pair each state with a concise explanation and timestamp.

### UX-005 — Skip link is present but page landmarks are not consistently named
- **Area:** Global accessibility landmarks
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Accessibility
- **Evidence:** [skip-link focus](screenshots/47-overview-skip-link-focus-desktop-light.png), [users desktop light](screenshots/04-users-desktop-light.png)
- **Observed:** The skip link receives focus and is visible on Tab, but several pages use generic regions or duplicate “Users” regions rather than one clearly named main content landmark.
- **Expected / why it matters:** Keyboard and screen-reader users need predictable landmarks and a single clear page destination.
- **Suggested direction:** Ensure `main` has a stable accessible name where needed and avoid duplicate region names unless they identify distinct content.

### UX-006 — Raw implementation terms are visible in user actions
- **Area:** User detail action dialogs
- **Viewport/theme:** desktop-dark
- **Severity:** High
- **Category:** Copy
- **Evidence:** [edit user dialog](screenshots/07-user-edit-dialog-dark.png), [reset password dialog](screenshots/08-reset-password-dialog-dark.png)
- **Observed:** Dialogs expose “users.update” and “users.reset_password” as required capabilities and use “Confirm action” rather than a human-readable action label.
- **Expected / why it matters:** Internal capability identifiers are not actionable language for administrators and increase the perceived risk of making a change.
- **Suggested direction:** Translate capabilities into plain-language prerequisites; keep technical identifiers behind an optional details disclosure.

### UX-007 — Destructive and sensitive actions share the same visual weight
- **Area:** User/device action controls
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Forms
- **Evidence:** [user detail](screenshots/05-user-detail-desktop-light.png), [device detail](screenshots/13-device-detail-desktop-light.png)
- **Observed:** Edit, password reset, group removal, MFA reset, remote lock, restart, retire and wipe actions appear as similarly weighted buttons in the same content flow.
- **Expected / why it matters:** High-impact actions need clear danger styling, separation and intent confirmation to prevent accidental activation.
- **Suggested direction:** Group routine versus destructive actions, add explicit risk copy and use destructive button semantics only where appropriate.

### UX-008 — The signed-in user area is informational but looks like a control cluster
- **Area:** Global header
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Low
- **Category:** Navigation
- **Evidence:** [overview desktop](screenshots/01-overview-desktop-light.png), [overview mobile](screenshots/24-overview-mobile-light.png)
- **Observed:** “Signed in as” and the user identity are presented next to actionable notification/theme controls, but there is no account menu or clear indication that the identity is non-interactive.
- **Expected / why it matters:** Consistent affordance prevents users from hunting for profile/session actions.
- **Suggested direction:** Either add an explicit account menu or visually distinguish the identity block from controls.

### UX-009 — Dark-mode control label describes the action, not the current state
- **Area:** Theme toggle
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** Low
- **Category:** Accessibility
- **Evidence:** [overview desktop light](screenshots/01-overview-desktop-light.png), [overview desktop dark](screenshots/02-overview-desktop-dark.png)
- **Observed:** The control alternates between “Enable dark mode” and “Disable dark mode” while the visible text says Light/Dark. This is technically understandable but requires interpretation and duplicates state/action semantics.
- **Expected / why it matters:** A clear switch should expose one stable label and an explicit checked state.
- **Suggested direction:** Use an accessible name such as “Theme” with `aria-checked` and visible “Light/Dark” state.

### UX-010 — Header status changes during navigation without explanation
- **Area:** Global header
- **Viewport/theme:** all desktop pages
- **Severity:** Medium
- **Category:** Feedback/States
- **Evidence:** [licenses](screenshots/09-licenses-desktop-dark.png), [exchange](screenshots/17-exchange-desktop-light.png), [activity](screenshots/15-activity-desktop-dark.png)
- **Observed:** Access snapshot and notification counts vary between routes while the user is moving through the app.
- **Expected / why it matters:** Unexplained changes look like race conditions or inconsistent data rather than a deliberate refresh model.
- **Suggested direction:** Document refresh timing and show a consistent “last checked” indicator for shell health.

## 4. Per-page findings

### `/overview`

### UX-011 — Overview’s primary cards are not actionable
- **Area:** Overview metric cards
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Medium
- **Category:** Usability
- **Evidence:** [overview desktop](screenshots/01-overview-desktop-light.png), [overview mobile](screenshots/24-overview-mobile-light.png)
- **Observed:** Users, Devices, License coverage and Permission health look like dashboard cards but have no visible link or button affordance.
- **Expected / why it matters:** Users reasonably expect summary cards to drill into the relevant section.
- **Suggested direction:** Make cards clearly clickable or style them as non-interactive statistics.

### UX-012 — “Devices Unavailable” is a high-salience metric with weak next step
- **Area:** Overview device card
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Feedback/States
- **Evidence:** [overview desktop light](screenshots/01-overview-desktop-light.png), [overview desktop dark](screenshots/02-overview-desktop-dark.png)
- **Observed:** The dashboard emphasizes “Unavailable / No verified tenant total” but does not link directly to the consent/setup explanation.
- **Expected / why it matters:** A dashboard warning should shorten the path from detection to resolution.
- **Suggested direction:** Add an explanatory tooltip or direct “Review access” affordance.

### UX-013 — Freshness copy is overly technical for the overview
- **Area:** Overview freshness banner
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Low
- **Category:** Copy
- **Evidence:** [overview desktop](screenshots/01-overview-desktop-light.png), [overview mobile](screenshots/24-overview-mobile-light.png)
- **Observed:** “Data freshness: cached/live. Retrieved [timestamp]” is shown as a full-width paragraph without saying what the user can do if data is stale.
- **Expected / why it matters:** Timestamp-only status does not answer whether refresh is available or needed.
- **Suggested direction:** Pair the state with a refresh action or a plain-language explanation.

### UX-014 — Needs-attention message is not linked to remediation
- **Area:** Overview “Needs attention” section
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Medium
- **Category:** Navigation
- **Evidence:** [overview desktop](screenshots/01-overview-desktop-light.png), [overview mobile menu](screenshots/25-overview-mobile-menu-light.png)
- **Observed:** “Workspace permissions need attention (19 of 24 available)” is a list item but not an obvious link.
- **Expected / why it matters:** Users should not need to guess whether to open Notifications, Settings or Microsoft Entra.
- **Suggested direction:** Make the item actionable and state the destination in its label.

### `/users`

### UX-015 — User table exposes principal names as dense primary content
- **Area:** Users table
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** Medium
- **Category:** Data/Tables
- **Evidence:** [users desktop light](screenshots/04-users-desktop-light.png), [users desktop dark](screenshots/03-users-desktop-dark.png)
- **Observed:** Long user principal names occupy a full column and visually compete with the display name; truncation or secondary treatment is not apparent.
- **Expected / why it matters:** Directory identifiers are useful but should not overpower the human-readable identity.
- **Suggested direction:** Keep display name primary, wrap/truncate identifiers with copy affordance and tooltip.

### UX-016 — “More”, “Open” and “Disable” actions are repetitive and visually ambiguous
- **Area:** Users table row actions
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Data/Tables
- **Evidence:** [users desktop light](screenshots/04-users-desktop-light.png)
- **Observed:** Each row repeats short action labels while the row itself is not clearly clickable. The user must map action columns to a specific row.
- **Expected / why it matters:** Repetitive controls increase scanning and activation errors in admin tables.
- **Suggested direction:** Make the name/open target primary and place secondary actions in a clearly labeled menu.

### UX-017 — Users page presents hidden filters in a collapsed “More filters” control
- **Area:** Users search/filter panel
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Usability
- **Evidence:** [users desktop dark](screenshots/03-users-desktop-dark.png)
- **Observed:** License and tenant-role inputs exist in the DOM/layout but are hidden behind a low-emphasis “More filters” affordance, making filter capability discoverability weak.
- **Expected / why it matters:** Administrators need to understand available filtering without hunting.
- **Suggested direction:** Show active filter count and preserve a clear expanded/collapsed state.

### UX-018 — User detail reports “Partial user details” despite a long complete-looking page
- **Area:** `/users/:userId`
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Feedback/States
- **Evidence:** [user detail light](screenshots/05-user-detail-desktop-light.png), [user detail dark](screenshots/06-user-detail-desktop-dark.png)
- **Observed:** Most identity, job, group and authentication sections render normally, while a mid-page status says “Partial user details” and separate sections report unavailable data.
- **Expected / why it matters:** The page does not make completeness boundaries clear; operators may assume all sections are trustworthy.
- **Suggested direction:** Add section-level provenance and a page summary that enumerates exactly which sections failed.

### UX-019 — User detail uses a large amount of vertical space before key sections
- **Area:** User detail header and summary
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Layout
- **Evidence:** [user detail light](screenshots/05-user-detail-desktop-light.png)
- **Observed:** Breadcrumbs, back link, profile header, management actions, partial-data banner and status summary push Identity and Job information far below the fold.
- **Expected / why it matters:** High-frequency admin tasks should expose identity and critical status sooner.
- **Suggested direction:** Compress redundant header/status layers and use a sticky summary or section navigation.

### UX-020 — Authentication methods expose internal class names as user-facing values
- **Area:** User detail authentication table
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Copy
- **Evidence:** [user detail light](screenshots/05-user-detail-desktop-light.png)
- **Observed:** Values such as `passwordAuthenticationMethod` and `microsoftAuthenticatorAuthenticationMethod` are displayed in code styling.
- **Expected / why it matters:** Microsoft Graph resource class names are implementation details and are not useful to most administrators.
- **Suggested direction:** Map to “Password” and “Microsoft Authenticator” while retaining technical details in an optional inspector.

### UX-021 — User detail contains disabled/failed sections without an inline retry per section
- **Area:** Roles/PIM and Associated devices sections
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Feedback/States
- **Evidence:** [user detail light](screenshots/05-user-detail-desktop-light.png)
- **Observed:** Roles and PIM says unavailable, Associated devices says unavailable, but only the page-level retry is prominent.
- **Expected / why it matters:** A page-level retry may refetch healthy sections unnecessarily and does not communicate which dependency is failing.
- **Suggested direction:** Provide section-level retry and reason codes translated into plain language.

### UX-022 — Dialog confirmations are capability-centric and visually under-explain consequences
- **Area:** Edit user and reset password dialogs
- **Viewport/theme:** desktop-dark
- **Severity:** High
- **Category:** Forms
- **Evidence:** [edit dialog](screenshots/07-user-edit-dialog-dark.png), [reset password dialog](screenshots/08-reset-password-dialog-dark.png)
- **Observed:** Dialogs show target/proposed change/capability and audit wording, but no concise impact summary or clear confirmation verb.
- **Expected / why it matters:** Sensitive actions need a predictable review step that emphasizes outcome and reversibility.
- **Suggested direction:** Use action-specific confirmation text, consequence summary and a clearly destructive/critical style where appropriate.

### `/licenses`

### UX-023 — License inventory relies on raw SKU names and GUIDs
- **Area:** License inventory table
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Copy
- **Evidence:** [licenses desktop light](screenshots/11-licenses-desktop-light.png), [licenses desktop dark](screenshots/09-licenses-desktop-dark.png)
- **Observed:** Multiple rows show “Product name unavailable”, technical SKU labels and full SKU IDs as the main license identity.
- **Expected / why it matters:** Operators need recognizable product names and seat meaning, not catalog implementation identifiers.
- **Suggested direction:** Resolve display names where possible and move SKU IDs to secondary copy/copyable details.

### UX-024 — Assigned-users tab changes context without a visible license selector
- **Area:** License view tabs
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Navigation
- **Evidence:** [assigned users](screenshots/10-licenses-assigned-users-desktop-dark.png)
- **Observed:** The Assigned users tab opens directly into one SKU’s roster; the selected license is expressed by a raw SKU label rather than an explicit selection control.
- **Expected / why it matters:** Users need clear context for which product roster they are viewing.
- **Suggested direction:** Add a named license selector or preserve the originating row context in the heading.

### UX-025 — License seat metrics are not visually explained
- **Area:** License inventory table
- **Viewport/theme:** desktop-light
- **Severity:** Low
- **Category:** Data/Tables
- **Evidence:** [licenses desktop light](screenshots/11-licenses-desktop-light.png)
- **Observed:** Purchased, Assigned and Available are presented as bare numbers with no tooltip/definition or percentage utilization.
- **Expected / why it matters:** Seat management benefits from immediate interpretation, especially when scanning multiple SKUs.
- **Suggested direction:** Add definitions and an at-a-glance utilization indicator.

### `/devices`

### UX-026 — Device list includes a raw device GUID directly beneath the device name
- **Area:** Devices table
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Data/Tables
- **Evidence:** [devices desktop light](screenshots/12-devices-desktop-light.png)
- **Observed:** The row puts a full GUID under the friendly device name, consuming space and increasing visual noise.
- **Expected / why it matters:** Device names and compliance should be primary; IDs are secondary lookup data.
- **Suggested direction:** Hide IDs behind copy/details affordances or use truncated IDs with tooltip.

### UX-027 — Device detail exposes several raw IDs as primary facts
- **Area:** `/devices/:id` overview
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Copy
- **Evidence:** [device detail light](screenshots/13-device-detail-desktop-light.png), [device detail dark](screenshots/14-device-detail-desktop-dark.png)
- **Observed:** Device ID, primary user ID and Entra device ID are shown as full GUID values in the main Overview panel.
- **Expected / why it matters:** Full IDs make the page feel like an API inspector and make it harder to find operational information.
- **Suggested direction:** Use labeled copyable ID rows in a secondary “Technical details” section.

### UX-028 — Recovery section repeats scope requirements and uses raw permission jargon
- **Area:** Device recovery data
- **Viewport/theme:** desktop-light
- **Severity:** High
- **Category:** Copy
- **Evidence:** [device detail light](screenshots/13-device-detail-desktop-light.png)
- **Observed:** BitLocker and Windows LAPS each repeat long scope requirements, including basic/all variants, and link to “Open Setup” twice.
- **Expected / why it matters:** Repetition and scope codes obscure the decision the operator needs to make.
- **Suggested direction:** Summarize the access blocker once with a plain-language explanation and one setup action.

### UX-029 — Remote actions lack visible safety framing
- **Area:** Device detail remote actions
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** Critical
- **Category:** Forms
- **Evidence:** [device detail light](screenshots/13-device-detail-desktop-light.png), [device detail dark](screenshots/14-device-detail-desktop-dark.png)
- **Observed:** Sync, Remote lock, Restart, Retire and Wipe appear together as same-sized buttons in a flat row; no warning, required reason or confirmation is visible before activation.
- **Expected / why it matters:** Wipe/retire are high-impact operations that must be unmistakably separated from routine sync/restart.
- **Suggested direction:** Separate action groups, add risk labels and require explicit confirmation/reason for destructive actions.

### UX-030 — Device freshness and last check-in use different date presentations
- **Area:** Devices summary and detail
- **Viewport/theme:** desktop-light
- **Severity:** Low
- **Category:** Consistency
- **Evidence:** [devices desktop light](screenshots/12-devices-desktop-light.png), [device detail light](screenshots/13-device-detail-desktop-light.png)
- **Observed:** Summary cards use a long date/time string while other status areas use different line wrapping and labels.
- **Expected / why it matters:** Consistent date formatting helps compare devices and identify stale signals.
- **Suggested direction:** Standardize timestamp formatting and include relative age.

### UX-031 — Disabled recovery controls do not explain the immediate next action well enough
- **Area:** Device BitLocker/LAPS cards
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Feedback/States
- **Evidence:** [device detail light](screenshots/13-device-detail-desktop-light.png)
- **Observed:** “Load … metadata” is disabled, while setup links are buried inside long explanatory paragraphs.
- **Expected / why it matters:** A disabled control without a nearby concise reason feels broken.
- **Suggested direction:** Place the blocker directly next to the disabled action and offer one clear setup path.

### `/services/exchange`

### UX-032 — Disabled module route has no direct recovery action
- **Area:** Exchange module-disabled state
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** High
- **Category:** Navigation
- **Evidence:** [Exchange desktop light](screenshots/17-exchange-desktop-light.png), [Exchange mobile](screenshots/32-exchange-mobile-light.png)
- **Observed:** The page says to ask an administrator to enable the module in Workspace settings, but provides no link to that section or return action.
- **Expected / why it matters:** A blocked route should make recovery one click away.
- **Suggested direction:** Add “Open Workspace settings” and “Back to overview” actions.

### UX-033 — Exchange is absent from primary navigation while direct route remains reachable
- **Area:** Navigation/module IA
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Navigation
- **Evidence:** [Exchange desktop light](screenshots/17-exchange-desktop-light.png), [settings modules](screenshots/20-settings-desktop-light.png)
- **Observed:** Exchange is configured as a module but not visible in the sidebar when disabled; direct URL produces a blocked page.
- **Expected / why it matters:** Hidden capability and direct-link error states should be intentionally connected.
- **Suggested direction:** Show disabled modules in a clearly labeled settings-only area or provide a consistent unavailable-route pattern.

### `/activity` and `/audit`

### UX-034 — Audit table exposes raw correlation IDs and JSON blobs
- **Area:** Activity/audit table
- **Viewport/theme:** desktop-dark, desktop-light
- **Severity:** High
- **Category:** Data/Tables
- **Evidence:** [activity desktop dark](screenshots/15-activity-desktop-dark.png), [activity desktop light](screenshots/16-activity-desktop-light.png)
- **Observed:** Target GUIDs, correlation IDs and serialized safe metadata are shown directly in table cells.
- **Expected / why it matters:** Raw audit payloads are difficult to scan and expose implementation details before the user asks for them.
- **Suggested direction:** Use human-readable summaries with expandable technical metadata.

### UX-035 — Activity filters use internal field language
- **Area:** Activity filters
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Copy
- **Evidence:** [activity desktop dark](screenshots/15-activity-desktop-dark.png)
- **Observed:** “Actor object ID” and placeholder `users.update` are aimed at API operators rather than workspace administrators.
- **Expected / why it matters:** Filters should describe the task (“Person”, “Action type”) while retaining technical search as an advanced option.
- **Suggested direction:** Rename labels and provide examples in plain language.

### UX-036 — Activity page does not make audit-versus-platform-log boundaries easy to understand
- **Area:** Activity explanation
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Copy
- **Evidence:** [activity desktop dark](screenshots/15-activity-desktop-dark.png)
- **Observed:** The explanatory paragraph says Microsoft 365 audit logs remain authoritative, but the page title is “Audit activity” and the distinction is easy to miss.
- **Expected / why it matters:** Users may assume this page is a complete audit source.
- **Suggested direction:** Use a stronger “Platform activity” title and a visible scope note.

### `/settings` and legacy deep links

### UX-037 — Settings is a very long, scan-heavy page
- **Area:** Workspace Settings
- **Viewport/theme:** desktop-light, desktop-dark, tablet-light, mobile-light
- **Severity:** High
- **Category:** Layout
- **Evidence:** [settings desktop light](screenshots/20-settings-desktop-light.png), [settings tablet](screenshots/37-settings-tablet-light.png), [settings mobile](screenshots/30-settings-mobile-light.png)
- **Observed:** Connection, General, Modules and Access sections stack into a page over 3,000 CSS pixels high; the section nav is not sticky.
- **Expected / why it matters:** Long administrative pages need persistent orientation and reduced cognitive load.
- **Suggested direction:** Use anchored sticky navigation, separate routes or collapsible sections with retained context.

### UX-038 — Settings presents save controls near high-impact configuration without clear dirty-state feedback
- **Area:** General and Modules settings
- **Viewport/theme:** desktop-dark
- **Severity:** High
- **Category:** Forms
- **Evidence:** [settings desktop dark](screenshots/19-settings-desktop-dark.png)
- **Observed:** “Save settings” and “Save modules” are visible while inputs/checkboxes appear immediately editable; there is no visible dirty state, unsaved-change warning or reset affordance.
- **Expected / why it matters:** Configuration changes need clear scope, pending state and prevention of accidental navigation loss.
- **Suggested direction:** Show changed/unchanged state and keep save actions scoped to the section.

### UX-039 — Invitation form is wide on desktop but vertically heavy on mobile
- **Area:** Access invitation form
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Medium
- **Category:** Responsive
- **Evidence:** [settings desktop light](screenshots/20-settings-desktop-light.png), [settings mobile](screenshots/30-settings-mobile-light.png)
- **Observed:** Email, display name, role and module access occupy a large form block; disabled Exchange access is mixed into selectable options.
- **Expected / why it matters:** Invitation flows should make required fields and available module choices immediately legible at every viewport.
- **Suggested direction:** Group role/access decisions and explain why unavailable modules are disabled.

### UX-040 — Tenant consent guidance is technically accurate but too long and repetitive
- **Area:** Expanded connection guidance
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Copy
- **Evidence:** [expanded connection guidance](screenshots/20-settings-desktop-light.png)
- **Observed:** The disclosure expands to several paragraphs and a long scope list, repeatedly explaining registration versus consent and PIM.
- **Expected / why it matters:** Operators need a concise decision tree, not a wall of implementation notes.
- **Suggested direction:** Convert into a short checklist with “Atea app owner”, “customer admin” and “PIM activation” steps.

### UX-041 — Legacy routes normalize to hash sections without preserving a clear route identity
- **Area:** `/settings/general`, `/workspace-settings`, `/workspace-access`, `/onboarding`
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Navigation
- **Evidence:** [settings general](screenshots/21-settings-general-desktop-light.png), [workspace settings](screenshots/22-workspace-settings-desktop-light.png), [workspace access](screenshots/23-workspace-access-desktop-light.png)
- **Observed:** Deep links redirect to `/settings#general` or `/settings#access`; browser history/back behavior and page title remain the same for distinct legacy destinations.
- **Expected / why it matters:** Deep links should be stable, shareable and understandable when reopened.
- **Suggested direction:** Keep canonical route aliases or expose the active section in the document title and heading context.

### UX-042 — Settings module controls are checkboxes for capability switches without impact summary
- **Area:** Modules section
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Forms
- **Evidence:** [settings desktop dark](screenshots/19-settings-desktop-dark.png)
- **Observed:** Users, Devices, Licenses and Exchange are presented as simple checkboxes, although toggling them changes which people can see modules.
- **Expected / why it matters:** Capability changes deserve explicit impact and audience context.
- **Suggested direction:** Add enabled/disabled status, affected audience and a short confirmation summary before save.

### `/identity`

### UX-043 — PIM guidance sends users to an external portal without return/check status
- **Area:** Identity/PIM guidance
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Medium
- **Category:** Navigation
- **Evidence:** [identity desktop](screenshots/42-identity-desktop-light.png)
- **Observed:** The page contains one external “Open Microsoft Entra PIM” link, but no indication of what role to activate, how to return, or how to refresh access.
- **Expected / why it matters:** Cross-system workflows need explicit before/after steps.
- **Suggested direction:** Add a short checklist, open-in-new-tab cue and “Refresh access” next step.

### UX-044 — Identity route is not represented in primary navigation
- **Area:** Identity/PIM information architecture
- **Viewport/theme:** desktop-light
- **Severity:** Low
- **Category:** Navigation
- **Evidence:** [identity mobile](screenshots/31-identity-mobile-light.png), [overview desktop](screenshots/01-overview-desktop-light.png)
- **Observed:** `/identity` is reachable by URL but has no visible sidebar entry, while PIM guidance is relevant to multiple unavailable actions.
- **Expected / why it matters:** Important remediation content should be discoverable from the surfaces that need it.
- **Suggested direction:** Link PIM guidance from permission warnings or settings instead of relying on a hidden route.

### Unknown route / 404

### UX-045 — 404 page has no direct recovery button
- **Area:** Unknown route
- **Viewport/theme:** desktop-light, mobile-light
- **Severity:** Medium
- **Category:** Feedback/States
- **Evidence:** [404 desktop](screenshots/44-404-desktop-light.png)
- **Observed:** The page says “Choose a section from the primary navigation” but provides no “Back to overview” or “Go home” action.
- **Expected / why it matters:** A not-found state should recover without requiring users to scan the sidebar.
- **Suggested direction:** Add one primary recovery link and preserve the attempted path in secondary text.

### UX-046 — 404 state is branded as “Preview route”
- **Area:** Unknown route
- **Viewport/theme:** desktop-light
- **Severity:** Low
- **Category:** Copy
- **Evidence:** [404 desktop](screenshots/44-404-desktop-light.png)
- **Observed:** “Preview route” sounds like a development artifact rather than a user-facing error state.
- **Expected / why it matters:** Error copy should be clear and production-appropriate.
- **Suggested direction:** Replace with a concise “This page isn’t available” label.

### Platform `/admin`

### UX-047 — Platform admin exposes tenant GUID as primary workspace content
- **Area:** `/admin` workspace list
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Copy
- **Evidence:** [admin desktop](screenshots/40-admin-desktop-light.png)
- **Observed:** The workspace card shows a full Tenant ID directly beneath the workspace label.
- **Expected / why it matters:** Tenant IDs are technical support data and should not dominate a workspace chooser.
- **Suggested direction:** Move the ID into technical details or a copyable secondary field.

### UX-048 — Platform admin has duplicate level-one headings
- **Area:** `/admin`
- **Viewport/theme:** desktop-light
- **Severity:** Medium
- **Category:** Accessibility
- **Evidence:** [admin desktop](screenshots/40-admin-desktop-light.png)
- **Observed:** “Atea platform administration” and “Workspaces” are both exposed as level-one headings.
- **Expected / why it matters:** A page should have one primary H1 and use H2 for the main content section.
- **Suggested direction:** Keep the product title as a banner/brand label and make “Workspaces” the sole page H1.

## 5. Cross-cutting patterns

### UX-049 — Technical identifiers are used as information architecture
- **Area:** Users, licenses, devices and activity
- **Viewport/theme:** all desktop themes
- **Severity:** High
- **Category:** Copy
- **Evidence:** [licenses](screenshots/11-licenses-desktop-light.png), [device detail](screenshots/13-device-detail-desktop-light.png), [activity](screenshots/16-activity-desktop-light.png)
- **Observed:** GUIDs, SKU IDs, Graph class names, capability strings and JSON are repeatedly presented in primary content instead of optional technical detail.
- **Expected / why it matters:** The product reads like a raw API console, reducing confidence and slowing routine administration.
- **Suggested direction:** Establish a consistent “Technical details” pattern with truncation, copy and disclosure.

### UX-050 — Loading, unavailable and stale states do not share a consistent component
- **Area:** Global data fetching
- **Viewport/theme:** desktop-light, desktop-dark
- **Severity:** High
- **Category:** Feedback/States
- **Evidence:** [licenses](screenshots/09-licenses-desktop-dark.png), [devices](screenshots/12-devices-desktop-light.png), [user detail](screenshots/05-user-detail-desktop-light.png), [settings](screenshots/20-settings-desktop-light.png)
- **Observed:** Loading appears as plain text, unavailable appears as alerts, stale appears as status text, and fresh data appears as a banner; shape, iconography and recovery controls vary.
- **Expected / why it matters:** Consistent state components reduce interpretation time and make failures feel intentional.
- **Suggested direction:** Standardize state banners with severity, timestamp, reason and next action.

### UX-051 — Dark mode is functionally present but not visibly audited for status semantics
- **Area:** Dark theme across data cards/tables/dialogs
- **Viewport/theme:** desktop-dark
- **Severity:** Medium
- **Category:** Visual
- **Evidence:** [overview dark](screenshots/02-overview-desktop-dark.png), [user detail dark](screenshots/06-user-detail-desktop-dark.png), [licenses dark](screenshots/09-licenses-desktop-dark.png)
- **Observed:** Dark theme changes surfaces and text, but status chips/alerts and dense table content remain visually similar in emphasis; no theme-specific legend or contrast cue is visible.
- **Expected / why it matters:** Dark mode should preserve hierarchy and status readability, not merely invert surfaces.
- **Suggested direction:** Recheck semantic color tokens and contrast for warning/success/unavailable states in both themes.

### UX-052 — Action labels are inconsistent across equivalent navigation patterns
- **Area:** Back/open/continue actions
- **Viewport/theme:** desktop-light
- **Severity:** Low
- **Category:** Consistency
- **Evidence:** [user detail](screenshots/05-user-detail-desktop-light.png), [device detail](screenshots/13-device-detail-desktop-light.png), [settings](screenshots/20-settings-desktop-light.png)
- **Observed:** Equivalent actions use “← Users”, “← Back to devices”, “Continue to overview” and “Open Setup”.
- **Expected / why it matters:** Consistent verbs and placement reduce navigation friction.
- **Suggested direction:** Adopt a shared back-link and setup-link pattern.

### UX-053 — Tables lack an explicit compact/dense mode choice
- **Area:** Users, licenses, devices, activity tables
- **Viewport/theme:** desktop-light, laptop-light
- **Severity:** Low
- **Category:** Data/Tables
- **Evidence:** [users laptop](screenshots/38-users-laptop-light.png), [devices laptop](screenshots/39-devices-laptop-light.png)
- **Observed:** Rows are tall enough to make scanning multiple records slow, while technical values still wrap or occupy large cells.
- **Expected / why it matters:** Admin tables should optimize for comparison and efficient scanning.
- **Suggested direction:** Offer a compact density or better secondary-value treatment.

### UX-054 — 200% zoom produces overflow on the Users experience
- **Area:** `/users`
- **Viewport/theme:** 200% zoom, light
- **Severity:** High
- **Category:** Accessibility
- **Evidence:** [users laptop](screenshots/38-users-laptop-light.png)
- **Observed:** At a 2× CSS zoom simulation, the Users page measured `scrollWidth: 802` versus `clientWidth: 713`; the body measured 1604px wide. Settings stayed within the root width but its body expanded to 1448px, confirming the layout becomes very wide at enlarged text.
- **Expected / why it matters:** Content should remain usable without two-dimensional scrolling at 200% zoom for ordinary page content.
- **Suggested direction:** Reflow table actions and long identifier columns into stacked or contained layouts at enlarged text sizes.

## 6. Console/network errors table

| Route/page | Console or network evidence | UX impact |
|---|---|---|
| All inspected customer routes | `GET /favicon.ico → 401` repeated in Playwright console | Persistent error noise; likely missing public asset configuration. |
| `/overview`, `/users`, `/licenses`, `/devices`, `/activity`, `/settings`, `/identity`, `/services/exchange`, `/404` | Core API requests observed as 200 during the signed-in session | Data paths are reachable, but page-level permission/availability states remain inconsistent. |
| `/devices/:id` | Device, authentication and workspace APIs returned 200; recovery controls remained unavailable | The UI should distinguish “API responded” from “feature permission denied”. |
| `/admin` | Console reported 1 error and 2 warnings during route load; no destructive flow opened | Admin shell was reachable; exact warning payload was not needed for read-only inspection. |

## 7. Accessibility summary

- Skip link is keyboard reachable and visibly appears on the first Tab press.
- Major pages expose H1 headings, but `/admin` exposes duplicate level-one headings.
- Tables generally expose column headers and named actions; technical content is often too verbose for quick assistive-technology scanning.
- Dialogs expose labels and a Cancel action; destructive/sensitive action copy is not sufficiently specific.
- Focus behavior was observable for the skip link and dialog Cancel button; a full exhaustive focus audit of every interactive row action was outside the read-only pass.
- Icon-only controls were not prominent in the inspected shell; the theme/menu controls had accessible names.
- Long settings and detail pages need stronger section navigation for keyboard and screen-reader users.

## 8. Responsive summary

| Page | Mobile 390×844 | Tablet 820×1180 | Laptop 1280×720 |
|---|---|---|---|
| Overview | No horizontal overflow; header consumes substantial vertical space; menu expands correctly. | No horizontal overflow; main content narrows to ~580px. | Not separately spot-checked. |
| Users | No horizontal overflow; content transforms away from a desktop table, but visible mobile row semantics need stronger verification. | Captured; dense data remains difficult to compare. | No horizontal overflow measured. |
| Licenses | No horizontal overflow; inventory becomes a long stacked experience. | Not captured. | Not captured. |
| Devices | No horizontal overflow; filters and records become vertically long. | Captured; dense record layout persists. | No horizontal overflow measured. |
| Activity | Captured; filter/table content becomes a long scroll. | Not captured. | Not captured. |
| Settings | Captured; the already long page becomes especially scroll-heavy. | Captured; main content narrows while sections remain long. | Not captured. |
| Identity | Captured; short page but external workflow remains unclear. | Not captured. | Not captured. |
| Exchange / 404 | Captured; no overflow, but blocked/error states lack recovery CTA. | Not captured. | Not captured. |

Measured overflow was `scrollWidth === clientWidth` on the inspected overview, users, licenses, tablet overview and laptop users pages; no horizontal overflow was observed in the tested states.
At a 200% zoom simulation, `/users` exceeded the viewport width (`scrollWidth 802` vs `clientWidth 713`), so the no-overflow result does not hold for enlarged text.

## 9. Prioritised backlog

| ID | Title | Severity | Effort | Area |
|---|---|---|---|---|
| UX-029 | Add safety framing and confirmations for remote device actions | Critical | M | Device detail |
| UX-003 | Consolidate permission failures into one actionable health model | High | L | Global/data states |
| UX-049 | Move technical identifiers into optional technical details | High | L | Cross-cutting |
| UX-050 | Standardize loading/unavailable/stale state components | High | M | Cross-cutting |
| UX-018 | Clarify partial user detail completeness | High | M | User detail |
| UX-023 | Resolve license display names and demote SKU IDs | High | M | Licenses |
| UX-037 | Split or improve orientation on long settings page | High | L | Settings |
| UX-034 | Replace raw audit payloads with summaries and expandable details | High | M | Activity |
| UX-032 | Add recovery actions to disabled Exchange route | High | S | Exchange |
| UX-038 | Add dirty-state and impact feedback to settings saves | High | M | Settings |
| UX-054 | Fix 200% zoom overflow on Users | High | M | Accessibility |
| UX-006 | Translate capability identifiers in action dialogs | High | M | User actions |
| UX-028 | Simplify recovery permission explanation | High | M | Device detail |
| UX-041 | Preserve canonical deep-link identity for settings sections | Medium | M | Navigation |
| UX-015 | Improve directory identifier hierarchy in user table | Medium | M | Users |
| UX-016 | Consolidate repetitive row actions | Medium | M | Users |
| UX-036 | Clarify platform activity versus Microsoft 365 audit logs | Medium | S | Activity |
| UX-042 | Explain module-switch impact before saving | Medium | M | Settings |
| UX-043 | Add return/status steps around external PIM workflow | Medium | S | Identity |
| UX-045 | Add direct recovery CTA to 404 state | Medium | S | Error handling |
| UX-002 | Fix unauthenticated favicon response | Low | S | Global shell |
| UX-046 | Replace “Preview route” error copy | Low | S | Error handling |

## 10. Open questions for the brainstorming agent

1. Which permission and consent states should be treated as first-class product states, and which should be hidden from everyday operators?
2. What is the intended audience split between workspace owners, customer administrators and delegated operators?
3. Should technical IDs remain discoverable via copy/details, and which ones are genuinely needed in routine workflows?
4. Are remote lock/restart/retire/wipe intended to share one action framework, or should destructive operations be isolated into a separate incident workflow?
5. Should settings become separate canonical routes, or should the current page remain a single guided setup flow with sticky section navigation?
6. What is the desired source-of-truth language for platform activity versus Microsoft 365 audit logs?
7. Which disabled modules should be visible in navigation, and what should the disabled-route recovery pattern be?
8. What are the supported mobile table interaction patterns: cards, expandable rows, horizontal scroll, or a dedicated compact list?
9. Which warning states can be resolved by the signed-in administrator versus requiring Atea/app-owner intervention?
10. Should the platform admin console use the same shell and heading conventions as the customer workspace?
