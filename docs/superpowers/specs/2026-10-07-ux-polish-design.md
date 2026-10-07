# UX polish design — professional, consistent, easy to use

- **Date:** 2026-10-07
- **Status:** Design approved for planning (autopilot; decisions recorded in §10)
- **Input:** `docs/ux-inspection/report.md` (UX-001…UX-054, screenshots in `docs/ux-inspection/screenshots/`)
- **Builds on:** `docs/superpowers/specs/2026-09-25-workspace-ux-redesign-design.md`. Its principles still apply:
  - Workspace module access is separate from Microsoft delegated authorization.
  - The server is the authority.
  - No new Microsoft 365 operations.
  - Notifications are the home for issues.
  - `/settings` stays one page with legacy redirects.
  - The SKU friendly-name mapping is versioned in the API.
- **Brand rules:** `docs/design/atea-ui-usage.md`

## 1. Outcome and success criteria

**Outcome.** The app should feel like one calm, professional admin product. It should not feel like a set of debug views. Data states should look the same everywhere. Technical identifiers should be available but never be the headline. Destructive operations should be visibly separated and clearly confirmed. The shell should take less attention than the task.

**Success criteria.** The implementing agent verifies these with tests and a manual pass at 1440×900, 390×844 and 200% zoom, in both themes.

| # | Criterion | How verified |
|---|---|---|
| S1 | Every Critical and High finding is resolved: 18 IDs (UX-029 plus 17 High). | Traceability table (§9) and manual pass |
| S2 | No raw GUID, SKU ID, Graph class name, capability string (`users.update`) or JSON is shown in primary content. They appear only inside a "Technical details" disclosure or a copyable ID control. | vitest assertions on Users, User detail, Licenses, Devices, Device detail, Activity, Admin and dialogs (§8) |
| S3 | Successful (fresh) data shows no coloured banner. It shows one quiet status line with a relative time. Only stale, partial, unavailable and permission states use colour. | vitest: `DataFreshness` with `fresh` renders no `role="alert"` and no `data-tone="success"` banner |
| S4 | Each surface has one visual primary button. Danger actions are visually separated from routine actions and always confirmed with an action-specific verb. | vitest: device remote actions render routine and danger groups; the confirm button text equals the action label |
| S5 | The desktop sidebar fills the viewport height. The mobile header is at most 64px tall when the menu is closed. | Manual pass and screenshot comparison |
| S6 | `/users` at 200% zoom (viewport 720 CSS px wide) has `document.documentElement.scrollWidth <= clientWidth`. | Manual check in DevTools; Playwright check if the E2E harness allows it |
| S7 | `GET /favicon.ico` and `/favicon.svg` no longer produce a 401 in the console. | Manual check against a local `dotnet run`, or the deployed build |
| S8 | Text contrast is at least 4.5:1 and non-text/status-indicator contrast at least 3:1, in light and dark themes. | Token table (§3.1) checked with a contrast tool and recorded in the PR description |
| S9 | `npm test`, `npm run test:behavior`, `npm run build` (tsc and vite) and the API tests all pass. Tests that asserted removed copy are updated, not deleted. | CI and local runs |

## 2. Approaches considered

**A. Page-by-page patching.** Fix each finding where it appears: rename strings, hide IDs and add CSS overrides per page.
- *Pros:* each change is small and low-risk.
- *Cons:* `theme.css` is already about 2,935 lines of append-only overrides, with duplicated `.user-detail-page`, `.filter-chips`, `.pagination-controls` and `.table-action` rules. Patching adds more of the same, so the inconsistency findings (UX-004, 007, 049, 050, 051, 052) would come back on the next feature.

**B. Foundations-first, evolve existing shared components (chosen).**
1. Add a small semantic layer: spacing, type, radius, elevation, status and button hierarchy tokens.
2. Upgrade the existing shared components in place, keeping their names and backward-compatible props: `WorkspaceDataState`, `DataFreshness`, `WorkspacePageHeader`, `StatusBadge`, `ConfirmationDialog`, `ThemeToggle`, `TenantContextHeader`, `AppShell` and `PrimaryNav`.
3. Add four small new primitives: `TechnicalDetails`, `CopyValue`, `MetricCard` and `ActionGroup`. Add two formatting modules: `dateTime` and `humanize`.
4. Migrate pages onto them.
- *Pros:* fixes root causes once and keeps import sites stable. It fits one PR because most page changes become "use the shared thing". No dependencies are added.
- *Cons:* touches many files. Changed copy needs test updates.

**C. Adopt a component library** (for example Radix, Fluent UI or MUI) and re-skin it with Atea tokens.
- *Pros:* mature accessible primitives.
- *Cons:* a heavy dependency and a rewrite of every page. Theming conflicts with the Atea tokens. It would not fit one PR and violates the "no new UI framework" constraint. Rejected.

**Why B.** Most High findings share a few root causes:
- No shared state component (UX-050).
- No technical-detail pattern (UX-049).
- No button or danger hierarchy (UX-007, UX-029).
- No status vocabulary (UX-004).

Fixing those once resolves about 30 findings almost mechanically. Keeping component names means that `UsersPage`, `DevicesPage` and others change mostly by props and copy, which keeps the PR reviewable.

**CSS strategy for B (decision).**
- Do **not** rewrite `theme.css`.
- Add `src/Web/src/styles/foundations.css` for tokens and base element rules.
- Add `src/Web/src/styles/components.css` for shared component and shell rules.
- Import both in `src/Web/src/main.tsx` **after** `./styles/theme.css`, so they win by source order without `!important`.
- When a rule in `theme.css` is superseded and fully replaced, delete the old rule in the same commit, especially duplicates of the same selector. Do not otherwise refactor `theme.css`.
- Keep `.button--primary`, `.page-action-bar` and `.detail-card` defined in `theme.css`, because `tests/Web.UnitTests/theme-and-catalog.test.mjs` asserts they exist there.

## 3. Design system foundations

### 3.1 Tokens (`styles/foundations.css`)

Every new token is a `--uw-*` alias defined in `:root` and overridden in `:root[data-theme='dark']`. Existing `--uw-*` colour tokens in `theme.css` (lines 3–57) are reused, not renamed.

**Spacing.** Use the Atea scale (`--atea-space-*`) through role aliases:

| Token | Value | Use |
|---|---|---|
| `--uw-gap-2xs` | `var(--atea-space-1)` | Icon to text |
| `--uw-gap-xs` | `var(--atea-space-2)` | Inline groups, chips |
| `--uw-gap-sm` | `var(--atea-space-3)` | Form field internals, table cell padding (vertical) |
| `--uw-gap-md` | `var(--atea-space-4)` | Card padding (mobile), stack gap inside a card |
| `--uw-gap-lg` | `var(--atea-space-6)` | Card padding (desktop), gap between cards |
| `--uw-gap-xl` | `var(--atea-space-8)` | Gap between page sections |

**Type scale.** Map to existing `--atea-ui-*` and `--atea-text-*` sizes and pick the nearest existing step.

| Token | Use | Weight |
|---|---|---|
| `--uw-font-page-title` | H1 in page header (about 1.75rem) | 700 |
| `--uw-font-section-title` | H2 card or section title (about 1.125rem) | 600 |
| `--uw-font-body` | Body and table text (about 0.9375rem) | 400 |
| `--uw-font-meta` | Status lines, secondary values, eyebrow (about 0.8125rem) | 400 or 600 (eyebrow) |
| `--uw-font-metric` | Metric value (about 2rem) | 700, `font-variant-numeric: tabular-nums` |

**Radius.**
- `--uw-radius-sm` (4px): chips and inputs.
- `--uw-radius-md` (8px): buttons and cards.
- `--uw-radius-lg` (12px): dialogs and popovers.

**Elevation.** These are new and replace the hardcoded shadows in `theme.css` that the touched rules use.
- `--uw-elevation-0`: none (cards use a border only).
- `--uw-elevation-1`: subtle (sticky header, sticky table header).
- `--uw-elevation-2`: menus and popovers (`ActionMenu`, `NotificationsMenu`).
- `--uw-elevation-3`: dialogs.

In dark mode, elevation uses darker and more opaque shadows plus a 1px `--uw-border` outline, because shadows alone are invisible on dark surfaces.

**Status colours.** Use a triplet per tone in both themes: `--uw-status-{tone}-fg`, `-bg` and `-border`, for the tones `success`, `warning`, `danger`, `info` and `neutral`.
- Light theme: derive the values from the existing `--atea-*-bg` and `--atea-*-text` tokens.
- Dark theme: use the existing surface and text pairs at lines 31–57, and add a `-border` that is at least 3:1 against `--uw-surface`.
- `neutral` uses `--uw-surface-subtle`, `--uw-muted` and `--uw-border`.
- Tone must never be the only signal. Every status chip also has a text label, and an icon where one is defined in §3.3.

**Focus.** Use `--uw-focus-ring: 0 0 0 2px var(--uw-focus-gap), 0 0 0 4px var(--uw-focus)` and apply it with `:focus-visible` to buttons, links, inputs, `summary` and `[tabindex]`.

**Colour-usage rules.**
- Green (`--uw-primary`) is reserved for the **one** primary action per surface and for the active navigation indicator. It is not used for status banners, header pills or the theme toggle.
- Red danger tone is used only for destructive actions and danger zones, never for counts. The notification count badge uses the `warning` tone in both themes; today it is pink in dark mode.

### 3.2 Button hierarchy

These are the only allowed button styles.
- Every `<button>` in feature code gets exactly one variant class.
- A bare `button` (no class) is restyled to look like `secondary`, so any button that is missed still looks correct.
- Delete the unused `.button-primary` (hyphen) rule.

| Variant | Class | Look | Use |
|---|---|---|---|
| Primary | `button button--primary` | Solid green, white text | The single main action on a page or dialog (Create user, Save settings, the confirm button for non-destructive dialogs) |
| Secondary | `button button--secondary` | Surface background, 1px `--uw-border-strong`, heading-colour text | Refresh, Export, Edit, Retry, Cancel in dialogs |
| Tertiary | `button button--tertiary` (new; replaces `button--quiet`, which stays as an alias) | No border, link colour, underline on hover | Inline row actions, "Show more", back links that are buttons |
| Danger | `button button--danger` | **Outlined** red (`--uw-status-danger-fg` text and border on the surface) at rest; solid red only as the **confirm** button in a destructive dialog | Disable, Remove, Reset MFA, Retire, Wipe |
| Icon | `button button--icon` (new) | 2.5rem square, tertiary look, `aria-label` required | Theme toggle, notifications, copy, menu |

**Sizes.**
- Default `min-height: 2.5rem`.
- `.button--sm` is `min-height: 2rem` and is used in table rows and compact lists.
- The touch-target minimum of 24×24 CSS px (WCAG 2.5.8) is met by both sizes.
- `.table-action` becomes an alias of `button--tertiary button--sm`. Keep the class so existing selectors and tests still work.

**Placement rules.**
- Header actions are right-aligned in the order *secondary… then primary* (the primary is rightmost).
- Dialog footers are right-aligned as `[Cancel (secondary)] [Confirm verb (primary or danger)]`. On mobile they stack, with the primary on top.
- Cancel is never styled as a link. This fixes the edit dialog, where Cancel currently uses `button--quiet`.

### 3.3 Status vocabulary and badges (UX-004, UX-051)

Use one taxonomy everywhere and add it to `messages/en.ts` under `status*` keys:

| Internal value(s) | User label | Tone | Icon (inline SVG) | Where |
|---|---|---|---|---|
| `fresh`, `live` | "Up to date" | neutral (no banner) | none | Status line |
| `cached` | "Cached" | neutral | clock | Status line: "Cached · updated 4 min ago" |
| `stale` | "May be out of date" | warning | clock | Status line, plus warning banner only when older than the page's refresh window |
| `partial` / `partialData` | "Partly loaded" | warning | alert-triangle | `WorkspaceDataState kind="partial"` banner listing affected parts |
| `unavailable` | "Unavailable" | danger | alert-circle | `WorkspaceDataState kind="unavailable"` with Retry |
| `loading` | "Loading {thing}…" | neutral | spinner (CSS) | `WorkspaceDataState kind="loading"` |
| permission (`consent_required`, `hidden`, `pim_*`, `read_only`) | "Access needed" / "Read-only" | info | lock | `WorkspaceDataState kind="permission"` with one next-step link |
| entity status: Enabled / Disabled / Compliant / Noncompliant / Active / Eligible | as-is (sentence case) | success / neutral / success / warning / success / info | dot | `StatusBadge` |

**`StatusBadge`** (`components/StatusBadge.tsx`) does the following:
- Gains `tone="neutral"`.
- Renders a 6px dot plus the label.
- Uses the status tokens, so it has a visible border in dark mode.
- Is used for entity status in tables: account status, compliance and role assignment state.

### 3.4 Shared data-state components (UX-050, UX-003, UX-013, UX-018, UX-021)

**`WorkspaceDataState`** (evolve `components/WorkspaceDataState.tsx`; keep the name and the old `state` prop as an alias of `kind`):

```ts
type DataStateKind = 'loading' | 'empty' | 'unavailable' | 'partial' | 'permission' | 'stale';
type DataStateProps = {
  kind?: DataStateKind;          // preferred
  state?: 'loading' | 'empty' | 'unavailable'; // legacy alias, keep working
  title?: string;                // short heading, e.g. "Some sections couldn't load"
  message: string;               // one plain-language sentence
  affected?: string[];           // e.g. ['Roles and PIM', 'Associated devices'] (partial)
  action?: { label: string; href?: string; onClick?: () => void }; // single next step (permission/empty)
  onRetry?: () => void;          // unavailable | partial | stale
  retrying?: boolean;            // disables Retry, shows "Retrying…"
  compact?: boolean;             // inline variant for use inside a card/section
};
```

- **Anatomy.** Icon, then title (bold, optional), then message, then an optional `affected` list, then actions (`Retry` as secondary, `action` as a tertiary link).
- **Size.** Full variant: a bordered card with the tone's `-bg`. Compact variant: no background, left 3px tone border, smaller padding. Use compact inside detail sections.
- **Roles.** `unavailable` uses `role="alert"`. All other kinds use `role="status"`. Loading also sets `aria-busy="true"` on the region.
- **Copy rules.** No technical reason codes in `message`. If a correlation ID exists, show it inside `TechnicalDetails` below the message.
- **Retire `AsyncState.tsx`.** Verify it has no imports, then delete it.

**`DataFreshness`** (evolve `components/DataFreshness.tsx`) becomes a **quiet status line**, not a banner.
- **Props.** Keep the existing props. Add:
  - `source?: string`, a plain-language source such as "Microsoft Graph"
  - `onRefresh?: () => void`
  - `refreshing?: boolean`
- **Render.** For example: `[icon?] Up to date · Updated 3 min ago · Microsoft Graph   [Refresh]`.
  - The time is a `<time dateTime={iso} title={absolute}>` element.
  - The text uses `--uw-font-meta` and the `--uw-muted` colour.
- **Fresh** (and not partial): neutral, `role="status"`, no background.
- **Stale:** warning tone text and icon in the line, no full-width block.
- **Unavailable or partial:** the line shows the label. The page *also* renders a `WorkspaceDataState` for the explanation. `DataFreshness` itself never renders a full-width coloured block.
- **Placement.** It goes in the page header's `meta` slot (§3.6), so it appears once per page. Sections inside a page do not repeat it. This removes the per-section "Directory data is fresh" text on User detail and the repeated "Source: … Retrieved …" lines in Settings, Licenses and Device detail.

**Date formatting** (new `src/Web/src/format/dateTime.ts`, UX-030):
- `formatDateTime(iso)` returns a localised absolute timestamp: `Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })`.
- `formatDate(iso)` returns `dateStyle: 'medium'`.
- `formatRelative(iso, now = Date.now())` returns, using `Intl.RelativeTimeFormat` with `numeric: 'auto'`:
  - "just now" for under 45s
  - "3 min ago"
  - "2 h ago"
  - "yesterday"
  - otherwise falls back to `formatDate`
- `<DateTime value relative? />` is a tiny component that renders `<time>` with the relative or absolute text, and the other form in `title`.
- Replace every `new Date(x).toLocaleString()` / `toLocaleDateString()` in `features/**` and `components/**` with these. Metric cards, tables and status lines use relative time with an absolute title. Detail fields use absolute time.

### 3.5 Technical details pattern (UX-049, UX-006, UX-020, UX-023, UX-026, UX-027, UX-034, UX-047)

**`TechnicalDetails`** (new `components/TechnicalDetails.tsx`):

```tsx
<TechnicalDetails summary="Technical details" items={[{ label: 'Device ID', value: device.id, copy: true }, …]} />
```

- Renders a native `<details className="technical-details"><summary>…</summary><dl>…</dl></details>`.
- It is collapsed by default.
- `summary` gets a chevron and the focus ring.
- `dd` values use a monospace font with `overflow-wrap: anywhere`. This is the **only** place `anywhere` breaking is allowed.
- Items with no value are omitted.
- If no items remain, the component renders nothing.

**`CopyValue`** (new `components/CopyValue.tsx`): `<CopyValue value={guid} label="Device ID" truncate />`.
- **Truncation.** When `truncate` is set, it shows a shortened form: the first 8 characters, "…", then the last 4. The full value goes in `title` and in a visually hidden span, so screen readers read the full value.
- **Copy button.** An icon button with the accessible name "Copy {label}". It calls `navigator.clipboard.writeText`.
  - On success it announces "Copied" through a polite live region for 2 seconds.
  - If the clipboard API is not available, the button is hidden. The full value stays selectable inside `TechnicalDetails`.

**Copy and terminology rules** (apply everywhere; add them to `docs/design/atea-ui-usage.md` under a new "Content rules" heading):
1. Primary content shows human names. The fallback order is: display name, then UPN or email, then a type-specific placeholder ("Unnamed device"). Never fall back to a GUID in a heading or table cell.
2. GUIDs, SKU IDs, part numbers (when a friendly name exists), Graph `@odata` or class names, capability keys, scope names, correlation and request IDs, and JSON go only in `TechnicalDetails` or `CopyValue`.
3. Capabilities are shown through `humanizeCapability(key)` (new `src/Web/src/format/humanize.ts`), for example:
   - `users.update` becomes "Edit users"
   - `users.reset_password` becomes "Reset passwords"
   - `devices.privileged.manage` becomes "Manage devices (remote actions)"

   Unknown keys become "Additional permission". The full mapping for every `Capability` value in `capabilities/capabilityTypes.ts` goes in `humanize.ts`, and there is a test that every member is mapped.
4. Authentication method types go through `humanizeAuthMethodType(type)`:

   | Graph type | Label |
   |---|---|
   | `passwordAuthenticationMethod` | "Password" |
   | `microsoftAuthenticatorAuthenticationMethod` | "Microsoft Authenticator" |
   | `phoneAuthenticationMethod` | "Phone" |
   | `fido2AuthenticationMethod` | "Passkey (FIDO2)" |
   | `windowsHelloForBusinessAuthenticationMethod` | "Windows Hello for Business" |
   | `emailAuthenticationMethod` | "Email" |
   | `softwareOathAuthenticationMethod` | "Software OATH token" |
   | `temporaryAccessPassAuthenticationMethod` | "Temporary Access Pass" |
   | `platformCredentialAuthenticationMethod` | "Platform credential" |
   | unknown | "Other method" |

5. Microsoft Graph scope names (for example `BitLockerKey.ReadBasic.All`) appear only in Settings → Connection's technical details, never in feature pages.
6. Sentence case for all labels and buttons. Buttons use **verb + object** ("Disable user", "Wipe device", "Save changes") and never "Confirm action".
7. Back links always read "Back to {Section}". Setup links always read "Open setup" (to `/settings#connection`), and PIM links always read "Open PIM guidance" (UX-052).

### 3.6 Page header anatomy (UX-005, UX-019, UX-052)

**`WorkspacePageHeader`** (evolve `components/WorkspacePageHeader.tsx`). It keeps `eyebrow`, `title`, `description`, `actions` and `children`, and adds:
- `backLink?: { label: string; href: string; onNavigate?: (path: string) => void }`. This renders above the eyebrow as a tertiary link "← Back to Users". If `onNavigate` is given, a click calls `preventDefault` and then `onNavigate`.
- `meta?: ReactNode`. This slot holds `DataFreshness` and/or a `StatusBadge`, rendered under the title row.
- The H1 always gets `id="page-title"`.

**Layout.**
```
[← Back to Users]                       (optional)
EYEBROW                                 (optional, meta font, uppercase)
Title (H1)                  [Secondary] [Secondary] [Primary]
Description (max 70ch, muted)
meta: Up to date · Updated 3 min ago · Microsoft Graph  [Refresh]
```
- Actions wrap below the title at 50rem or narrower.
- The header has no card background; it sits on the canvas.
- Content cards follow at a gap of `--uw-gap-lg`.

**Landmarks (UX-005).**
- `AppShell`'s `<main id="main-content">` gets `aria-labelledby="page-title"`.
- Pages must not wrap their whole content in `<section aria-label="…">`. Remove `aria-label` from page-level wrappers: `LicensesPage` ("License inventory"), `DeviceDetailPage` ("Device details"), and the duplicate "Users" region (keep the `ResponsiveDataView` table region label, which names a distinct scrollable region).
- `WorkspaceAccessPage`, `WorkspaceSettingsPage`, `WorkspaceModulesPage` and `OnboardingPage` keep their `embedded` H2 behaviour inside `/settings`.

### 3.7 Cards and metrics (UX-011, UX-012, UX-025, UX-030)

**Cards.**
- `.content-panel` and `.detail-card` get `--uw-surface`, a 1px `--uw-border`, `--uw-radius-md`, `--uw-elevation-0`, and padding `--uw-gap-lg` (`--uw-gap-md` at 50rem or narrower).
- Card titles are H2 at `--uw-font-section-title`.
- Content inside a card aligns to the top. Fix the empty top space in the Device detail Security and Hardware cards: grid children must not stretch their `dl` to a shared row height with vertical centring. Use `align-items: start`.

**`MetricCard`** (new `components/MetricCard.tsx`):

```ts
type MetricCardProps = { label: string; value?: string | number | null; detail?: string; href?: string; onNavigate?: (path: string) => void; linkLabel?: string; unavailableReason?: string; action?: { label: string; href: string } };
```

- **Linked card.** When `href` is set, the whole card is a link: the `<a>` wraps the label and value and gets an accessible name such as "Users: 42, open Users". It shows a hover border in `--uw-primary` and a "→" affordance in the corner.
- **No value.** When the value is null or undefined, the card shows a muted em dash at **body size** (not hero size), followed by the `unavailableReason` in meta text and an optional `action` link (for example "Review access"). The hero-size word "Unavailable" is never rendered.
- **Value styling.** Values use `--uw-font-metric` with tabular numerals.
- **Dates.** A date value is rendered with `formatRelative`, at section-title size, never hero size. This fixes "Last check-in".

### 3.8 Tables and responsive lists (UX-015, UX-016, UX-053 partial, UX-054)

These apply to `ResponsiveDataView` plus the `.users-table` and `.detail-table` CSS, through shared rules in `components.css`.

- **Layout and density.**
  - `table-layout: auto`.
  - The first column (the name) gets `min-width: 14rem`.
  - Numeric columns get the class `numeric` (`text-align: right; font-variant-numeric: tabular-nums`) on both `th` and `td`. This fixes the misaligned license numbers.
  - Cell padding is `--uw-gap-sm` × `--uw-gap-md`, which gives a row height of about 3rem. This is denser than today, partly addressing UX-053 without a toggle.
- **Primary cell.**
  - The primary entity name is a **link** (`<a href>` with SPA `onNavigate`), not a separate "Open" button column.
  - Secondary identifiers (UPN, email, model) appear under the name in `--uw-font-meta` muted text.
  - They have `overflow-wrap: break-word` and, in table mode, `text-overflow: ellipsis` with `max-width: 32ch`. The full value goes in `title`; compact mode shows it unclamped.
- **Remove the global rule.** Remove `.app-main code, .app-main a, .app-main td { overflow-wrap: anywhere; }` (around line 600 of `theme.css`). Replace it with `overflow-wrap: break-word` on `td` and `anywhere` only in `.technical-details dd` and `code`.
- **Row actions.**
  - The last column is "Actions", right-aligned, with `<span class="sr-only">` header text.
  - With more than one secondary action, use one `ActionMenu` whose label is "Actions" and whose accessible name is "Actions for {name}" (aria-label). The visible label is short.
  - With exactly one action, use a single tertiary `button--sm`.
  - Danger items in the menu come last, after a separator, with `danger: true`.
- **Sticky header.** `thead th` gets `position: sticky; top: 0` inside `.users-table-wrap` when the wrap scrolls.
- **Compact list switch.** Lower the switch from 64rem to **56rem**, still via `matchMedia` in `ResponsiveDataView`. At 200% zoom on a 1440 px viewport, the effective width is about 720px, so the compact list renders and there is no page-level horizontal overflow (UX-054).
- **Table wrapper.** The table wrapper keeps `overflow-x: auto` and `tabIndex=0` as a scroll-region safety net. The **page** (`.app-main`) gets `min-width: 0` and `overflow-x: clip` on grid children so a wide table never widens the body.
- **Pagination.** Shared `.table-pagination` layout: "Page 1 of 3" (meta, left) and `[Previous] [Next]` (secondary `button--sm`, right) in one row. Labels are "Previous" and "Next". The accessible names stay "Previous page" and "Next page" through `aria-label`.

### 3.9 Dialogs and confirmations (UX-006, UX-007, UX-022, UX-029)

**`ConfirmationDialog`** (evolve `components/ConfirmationDialog.tsx`; keep the existing props working).

New props:
- `confirmLabel?: string`. This is the verb label, for example "Disable user" or "Wipe device". Default: `title`.
- `consequence?: string`. One or two sentences on what will happen, shown before the details.
- `tone?: 'default' | 'danger'`.
- `requiredCapability` is now rendered via `humanizeCapability` under the label "Requires permission". The raw key moves into `TechnicalDetails`.

Layout, from top to bottom:
1. H2 title.
2. A `consequence` callout. For `tone="danger"`, it has the danger tone and an alert icon.
3. A `dl` with "Applies to: {target}" and "Change: {proposedChange}".
4. `children`, for the form fields.
5. The audit notice in meta text.
6. The destructive-phrase input. For example: "Type WIPE to confirm" is a proper label above a full-width input.
7. The reviewed checkbox: the checkbox sits **inline left of its label**, in one row. This fixes the layout bug where the box sits centred above the text. Apply it to every `label > input[type=checkbox]` via `.checkbox-field` (`display: flex; align-items: flex-start; gap: var(--uw-gap-xs)`). Also fix the global rule that makes `label` a block with the input stretched to `width: 100%`: checkboxes and radios get `width: auto`.
8. The footer: `[Cancel] [confirmLabel]`.
   - The confirm button is `button--danger` in its solid form when `tone="danger"`, otherwise `button--primary`.
   - While busy it reads "{confirmLabel}…" (for example "Wiping device…"); otherwise it shows the verb.

Other dialog rules:
- **Behaviour (keep).** Focus containment, Escape to cancel unless busy, `aria-modal`, initial focus on the first field or the Cancel button (never the destructive confirm), and focus returns to the trigger on close.
- **Size.** Width is `min(36rem, 100vw - 2rem)`. Height is `max-height: calc(100vh - 4rem)` with `overflow:auto` on the body and a sticky footer.
- **Remove "Confirm action".** Every caller passes `confirmLabel` and, for destructive actions, `consequence` and `tone="danger"`. Add the callers' copy to `messages/en.ts`. Callers are:
  - `UserEditDialog`
  - `PasswordResetDialog`
  - `TemporaryAccessPassDialog`
  - `RevokeSessionsDialog`
  - `GroupMembershipDialog`
  - `LicenseAssignmentDialog`
  - `UserCreateDialog`
  - the user disable confirmation in `UsersPage` / `UserDetailPage`
  - `AuthenticationMethodsSection` (remove method, reset MFA)
  - `DeviceDetailPage` and `DevicesPage` (remote actions)
  - `PimActivationDialog`, if it uses `ConfirmationDialog`

### 3.10 Action grouping and danger zones (UX-007, UX-029)

**`ActionGroup`** (new `components/ActionGroup.tsx`): `<ActionGroup title="Device actions" description=… tone="default|danger">{buttons}</ActionGroup>`.
- It renders an H3, a meta description and a button row.
- `tone="danger"` adds a danger-tone left border, the heading "Danger zone" by default, and a risk sentence.

Rules:
- Routine and recoverable actions go in a default group.
- Irreversible or high-impact actions go in a danger group, placed **last** on the page or section and visually separated by `--uw-gap-xl`.
- Each danger action has a one-line risk description beside it. For example, Wipe: "Erases all data and resets the device. Cannot be undone."

## 4. Shell

Files: `components/AppShell.tsx`, `components/PrimaryNav.tsx`, `components/TenantContextHeader.tsx`, `components/ThemeToggle.tsx`, `components/NotificationsMenu.tsx`, `notifications/workspaceIssues.ts`, `features/admin/AdminShell.tsx`, `styles/components.css` (new rules), `theme.css` (delete superseded header/nav/toggle rules around lines 358–600 and the mobile block near line 1909 only where replaced).

### 4.1 Header (UX-001, UX-003, UX-008, UX-010, UX-051)

Desktop layout (height ≈ 64px, sticky, `--uw-surface`, bottom border, `--uw-elevation-1` once scrolled is optional):

```
[Atea logo] | Workspace name (tenant)          [Access limited?] [🔔 2] [☾ switch] | (SH) Sondre Haugen
```

- **Remove the access-snapshot pill** ("Access snapshot fresh / Entra roles and Graph consent") from `TenantContextHeader`. When the capability snapshot is healthy, the header shows nothing about it.
- When the snapshot is degraded (stale, failed, or consent/PIM issues present), show one compact chip `Access limited` (warning tone, lock icon, `button--sm`), which opens the notifications panel. This makes the shell stable across routes (UX-010): the chip only appears/disappears when the underlying state changes, not with route-specific loading.
- **Notifications panel** (`NotificationsMenu`): add a footer status row "Access check: {status label} · checked {relative time} · [Refresh]" using the existing capabilities/notifications refresh. Each issue keeps its `safeAction` link (labels per §3.5 rule 7). Empty state: "No issues need your attention." The count badge uses `warning` status tokens (fixes pink badge in dark mode, UX-051) and is hidden at 0; the button's accessible name is "Notifications, {n} need attention".
- **Account area** (UX-008): initials avatar (32px circle, `--uw-surface-subtle`, heading text) + display name (truncate at 20ch, full name in `title`), separated from controls by a 1px vertical divider. It is informational (no hover styles, not focusable). No account menu is added (sign-out is not in scope; see §10).
- Workspace/tenant name: heading-colour text, truncate with ellipsis; tenant ID is not shown.

### 4.2 Theme toggle (UX-009 and coordinator observation)

`ThemeToggle` becomes a quiet switch, not a primary button:
- `button.button--icon.theme-toggle` with `role="switch"` (keep — asserted by `theme-and-catalog.test.mjs`), `aria-checked={isDark}`, stable accessible name **"Dark mode"** (via `aria-label`), visible content: sun/moon inline SVG + a small track/thumb (32×18px) in `--uw-border-strong` / `--uw-primary` when on.
- Tooltip/title: "Dark mode: on|off". No visible text label on desktop; in the mobile drawer it renders as a full row "Dark mode [switch]".
- Same component is used in `AdminShell`.

### 4.3 Sidebar (coordinator observation, UX-044, UX-033)

- **Full height bug:** `.app-body` is a grid `15rem 1fr`; surface/border currently sit on `.primary-nav`, whose height is its content (~490px). Move background `--uw-surface` and right border to the sidebar container (`.mobile-nav-container` at desktop widths), make it `position: sticky; top: var(--uw-header-height); height: calc(100vh - var(--uw-header-height)); overflow-y: auto; align-self: start`. Define `--uw-header-height: 4rem` in foundations.
- **Groups:** keep current group structure (Overview; People: Users, Licenses; Devices; Services: Exchange when enabled; Workspace: Activity, Settings). Group labels: `--uw-font-meta`, uppercase, muted, `--uw-gap-lg` top spacing.
- **Items:** 2.5rem tall, icon (inline SVG, 18px, `currentColor`) + label, `--uw-radius-sm`. Hover: `--uw-surface-subtle`. Active (`aria-current="page"`, already set): 3px `--uw-primary` left indicator, `--uw-surface-subtle` background, 600 weight. Icons are a small local set in new `components/icons.tsx` (overview, users, licenses, devices, mail, activity, settings, bell, sun, moon, lock, alert-triangle, alert-circle, clock, copy, chevron, external, menu, close, check) — hand-written SVG paths, `aria-hidden="true"`, no dependency.
- **Disabled modules** stay hidden (server authority; previous spec). UX-033 is resolved by the consistent unavailable-route pattern (§5.8) rather than showing disabled items in nav.
- **Identity/PIM (UX-044):** not added to nav. Instead it is linked from every permission state (`WorkspaceDataState kind="permission"` action), the notifications panel issues, the Overview "Needs attention" list, and the Settings → Connection section ("Open PIM guidance").
- Sidebar footer: optional small "Atea Unified Workplace" meta text; nothing else.

### 4.4 Mobile (≤ 50rem) (UX-019 indirectly, UX-039, S5)

- Header collapses to one row ≤ 64px: logo mark, truncated workspace name, notifications icon button, menu button (`aria-expanded`, `aria-controls`). Today the stacked header is ~380px tall.
- Menu opens a drawer (existing `.mobile-nav-container` toggle behaviour retained) containing: nav groups, then a divider, then "Dark mode" switch row, then the account row. Drawer closes on route change and Escape, returns focus to the menu button.
- `Access limited` chip collapses into a dot on the notifications button.

### 4.5 Breadcrumbs (UX-052, UX-049)

Keep `AppShell` breadcrumbs (asserted by `AppShell.test.tsx`: nav "Breadcrumbs", Settings › General › Services where Services is not a link).
- ID segments: for both `/users/:id` and `/devices/:id` the last crumb reads "Details" (today device GUIDs leak). Generalise the existing users rule to any segment matching a GUID/opaque-ID pattern.
- Parent crumbs link when they are registered routes (`/users`, `/devices`, `/licenses`, `/settings`).
- Breadcrumbs are hidden at ≤ 50rem (the page-header back link covers it).

## 5. Page-by-page changes

Each page adopts: `WorkspacePageHeader` (with `meta` = `DataFreshness`), `WorkspaceDataState` for every non-success state, `TechnicalDetails`/`CopyValue` for identifiers, the button hierarchy, and `dateTime` formatting. Below are page-specific changes only.

### 5.1 Overview — `features/overview/OverviewPage.tsx` (UX-011, UX-012, UX-013, UX-014)

- Header: eyebrow none, title "Overview", description one sentence, `meta` = `DataFreshness` with `onRefresh` = existing retry/reload. Remove the raw sentence "Data freshness: cached. Retrieved …" (UX-013).
- Metric row (`MetricCard`, responsive grid `repeat(auto-fit, minmax(14rem, 1fr))`):
  - Users → `/users` ("Open users"), Licenses → `/licenses` (value: assigned / total if available), Devices → `/devices`.
  - **Devices card:** the overview API has no device count (decision: do not add one). Render as a navigational card: label "Managed devices", value omitted, detail "View compliance and remote actions", link "Open devices". If the devices module is unavailable to the user, render the `unavailableReason` + "Review access" action (to `/identity` or `/settings#connection` per rule below). Never hero-size "Unavailable" (UX-012).
- "Needs attention" list (UX-014): each item is a row with tone icon, one sentence, and one action link:
  - PIM/role activation needed → "Open PIM guidance" (`/identity`).
  - Consent/permission missing → "Open setup" (`/settings#connection`) if `canManageSettings`, else "Open PIM guidance".
  - Partial data → "Retry" button calling the overview reload.
  - Empty → `WorkspaceDataState kind="empty"` compact: "Nothing needs your attention."
- Add a "Shortcuts" card (fills the empty area): links to the enabled modules' main tasks (e.g. "Find a user", "Review licenses", "Check device compliance", "Workspace settings" when permitted), built from the same nav/module list so disabled modules don't appear.

### 5.2 Users — `features/users/UsersPage.tsx`, `UsersTable.tsx`, `UserFilters.tsx` (UX-015, UX-016, UX-017, UX-054)

- Columns: **Name** (link to detail; UPN below in meta, truncated with `title`), **Status** (`StatusBadge` Enabled=success / Disabled=neutral), **Type** (Member/Guest), **Actions** (sr-only header).
- Remove the "More/Less" expanded row and the separate "Open" button (UX-016). Row actions: `ActionMenu` "Actions" (aria-label "Actions for {name}") with "Open details", separator, danger "Disable user" (or "Enable user"). Keep `PermissionState` explanation when disable is not permitted: the menu item is `disabled` with `description` "Requires: Disable users" (add optional `description?: string` to `ActionMenuItem`).
- Filters (UX-017): the "More filters" `<summary>` shows "More filters (2)" when advanced filters are active, and active filters always render as removable chips above the table (existing `.filter-chips` pattern; delete the duplicate CSS definition). Toolbar wraps (`flex-wrap`) — no fixed widths.
- 200% zoom (UX-054): achieved by §3.8 (compact list at ≤56rem, `min-width:0` on `.app-main` grid children, removing `overflow-wrap:anywhere`, wrapping toolbars, and the mobile header). Compact list item: name link, UPN meta, status badge, actions menu.
- Primary header action: "Create user" (primary) if permitted; "Export" (if present) secondary.

### 5.3 User detail — `features/users/UserDetailPage.tsx` + sections (UX-018, UX-019, UX-020, UX-021, UX-006, UX-007, UX-022)

- Header: `backLink` "Back to Users"; title = display name; description = UPN (meta); `meta` = account `StatusBadge` + `DataFreshness`. Remove the in-card "← Users" link.
- Header actions: "Edit user" (secondary), "Reset password" (secondary), and an `ActionMenu` "More actions" containing "Revoke sessions", "Issue Temporary Access Pass", separator, danger "Disable user". Exactly zero primary buttons on the page header (no single dominant action on a detail page) — decision.
- **Partial state (UX-018):** replace the generic "Partial user details" banner (around line 159) with `WorkspaceDataState kind="partial"` title "Some sections couldn't load", `affected` = names of failed sections (e.g. "Roles and PIM", "Authentication methods"), `onRetry` = existing detail refresh, label "Retry user details". If no section failed, no banner.
- **Per-section states (UX-021):** each section that fails renders `WorkspaceDataState kind="unavailable" compact` with Retry (calls the section's own loader if it has one, else the detail refresh). Permission-hidden sections render `kind="permission" compact` with "Open PIM guidance" / "Open setup".
- Remove per-section "Directory data is fresh" lines (freshness appears once in header).
- **Compression (UX-019, partial):** two-column grid at ≥ 72rem (`Profile` + `Account` left; `Groups`, `Roles and PIM`, `Licenses` right), single column below. Section cards use `--uw-gap-lg`. `dl` uses a two-column label/value grid (`max-content 1fr`). Sticky in-page section nav is deferred.
- **GroupsSection** fix: each group row = name (body, 600) over meta line (mail nickname only if it differs from the name, plus "Security"/"Microsoft 365" type if available); "Remove" tertiary-danger `button--sm` right-aligned. Fix `.record-list li` to `display:flex; justify-content:space-between; gap`. No more "haugentechhaugentech".
- **RolesAndPimSection** fix: role name + `StatusBadge` (`active` → "Active" success, `eligible` → "Eligible" info) via `humanizeAssignmentState`. No more "Global Readeractive".
- **AuthenticationMethodsSection** (UX-020): show `humanizeAuthMethodType(method.type)` + meta details (phone number masked as provided by API, device name, created date via `formatDate`). Raw `@odata` type moves into a per-method `TechnicalDetails`. "Issue Temporary Access Pass" = secondary; "Reset MFA" = danger outline in an `ActionGroup tone="danger"` at the bottom of the section with risk copy "Removes all registered methods except password. The user must register again."; per-method "Remove" = tertiary danger `button--sm` with confirmation.
- Dialogs (UX-006, UX-022): all user dialogs use `ConfirmationDialog` `confirmLabel`/`consequence`/`tone` per §3.9. E.g. Disable user: consequence "{name} will be signed out of new sessions and can't sign in until re-enabled."; Revoke sessions: "Signs {name} out of all apps and devices. They can sign in again immediately."; Reset password: "{name} gets a temporary password and must change it at next sign-in."; Edit user confirm "Save changes" (tone default).

### 5.4 Licenses — `features/licenses/LicensesPage.tsx` (UX-023, UX-024, UX-025)

- Remove outer `content-panel` page wrapper and the `aria-label="License inventory"` region (UX-005); header + tabs + table sit directly in main like other pages.
- Product column (UX-023): friendly name from API (`displayName`). When the API returns no friendly name, show the `partNumber` as the name (no red "Product name unavailable" label) with a neutral meta "Unrecognised product". SKU ID and part number go into a row-level `TechnicalDetails` (compact list) / `CopyValue` in an expandable row detail (table). API resolver additions in §6.2 reduce the fallback case.
- Numbers (UX-025): columns Total, Assigned, Available are `numeric` (right-aligned, tabular). Add a "Usage" column: `<meter min=0 max={total} value={assigned}>` styled bar (80px) + text "{assigned} of {total} ({pct}%)"; meter colour warning at ≥ 90%, danger at 100% only when available = 0 (tone also expressed in text "Fully assigned").
- Assigned-users tab (UX-024): add a labelled `<select>` "License" populated from loaded inventory (friendly names), default = currently selected SKU; changing it updates the view (existing state). Tab panel header reads "Users assigned {license name}".
- Pagination per §3.8. Source/"Retrieved" line → header `DataFreshness`.

### 5.5 Devices — `features/devices/DevicesPage.tsx` (UX-026, UX-030, UX-029 partial)

- Replace the green `DataFreshness` banner (around line 166) with the header meta line.
- Summary cards use `MetricCard`: Total, Compliant, Noncompliant (warning tone text when > 0), "Last check-in" uses `formatRelative` at section-title size with absolute `title` (UX-030).
- Device name cell: name link + meta "{OS} · {model}"; remove `<small>{device.id}</small>` (UX-026). Compact view: Device ID only in `TechnicalDetails`.
- Compliance column: `StatusBadge`. Last check-in column: relative time.
- Row actions: `ActionMenu` label "Actions", aria-label "Actions for {device name}"; routine items (Sync, Restart, Remote lock) first, separator, danger items (Retire, Wipe) with `description` risk text. All go through `ConfirmationDialog` using the same copy and tones as §5.6 (`tone="danger"` for Retire/Wipe; default tone for Sync/Restart/Lock). Keep existing drawer "Danger zone" grouping where present.

### 5.6 Device detail — `features/devices/DeviceDetailPage.tsx` (UX-027, UX-028, UX-029, UX-030, UX-031)

- Header: `backLink` "Back to Devices" (remove the "← Back to devices" action button), title = device name, description "{OS} {version} · {model}", `meta` = compliance `StatusBadge` + `DataFreshness` (replaces `SourceStamp`). Remove `aria-label="Device details"` page region.
- Overview card (UX-027): show Device name, Primary user (display name / UPN, linked to `/users/{id}` when an ID exists), Ownership, Enrolled, Last check-in. Remove Device ID, Primary user ID, Entra device ID from the card; add a `TechnicalDetails` card at the bottom with all three as `CopyValue`s plus serial number if present.
- Card grid `align-items: start` (top alignment fix).
- **Recovery (UX-028, UX-031):** BitLocker and LAPS cards each show the action button and, when blocked, a single inline blocker line directly under the disabled button: "{Humanized capability} isn't available to you." + one link ("Open setup" if the gap is consent and the user can manage settings; else "Open PIM guidance"). Replace per-capability `RecoveryGuidance` blocks listing scopes. If both cards are blocked for the same reason, show one `WorkspaceDataState kind="permission"` above both and keep the buttons disabled with `aria-describedby` pointing to it. Scope names appear only in Settings → Connection technical details.
- Recovery reason field: full-width labelled input "Reason (required, recorded in activity)", hint text below, `aria-describedby`; keep existing validation and API contract.
- **Remote actions (UX-029, Critical):**
  - `ActionGroup title="Device actions"`: Sync (secondary), Restart (secondary), Remote lock (secondary), each with one-line description.
  - `ActionGroup tone="danger" title="Danger zone"`: Retire ("Removes company data and management. Personal data stays."), Wipe ("Erases all data and resets the device to factory settings. This cannot be undone.") as `button--danger`. Placed last on the page.
  - Every remote action opens `ConfirmationDialog` with `confirmLabel` = action verb ("Restart device", "Lock device", "Retire device", "Wipe device"), `consequence` = the risk copy, `target` = device name + primary user, `tone="danger"` for Retire/Wipe (and Restart/Lock use default tone but still confirm). Sync confirms without the typed phrase (existing behaviour); Retire/Wipe keep the typed phrase + reviewed checkbox. Busy text "Wiping device…" etc. Success/failure surfaces as the existing status message, rendered as a `role="status"` inline notice next to the group.
  - No reason field for remote actions (API contract has no reason parameter; unchanged).

### 5.7 Exchange & disabled modules — `app/App.tsx` (~lines 132–134), `features/exchange/ExchangeOverviewPage.tsx` (UX-032, UX-033)

Introduce one `ModuleUnavailable` rendering (inline in `App.tsx` or a small `components/ModuleUnavailable.tsx`) used for module-disabled and workspace-access-denied routes:
- `WorkspacePageHeader` title = module name (e.g. "Exchange"), then `WorkspaceDataState kind="permission"` with title "Exchange is turned off for this workspace" / "You don't have access to Exchange", one-sentence reason, and actions:
  - `canManageModules` → primary "Open module settings" (`/settings#modules`); always secondary "Back to overview" (`/overview`).
  - otherwise → "Back to overview" + meta "Ask a workspace owner to turn it on."
- Uses existing `moduleDisabledTitle/Body` messages (updated copy).

### 5.8 Activity — `features/audit/AuditActivityPage.tsx` (UX-034, UX-035, UX-036)

- Title "Platform activity" (UX-036). Under the description, an info-tone compact note: "Shows actions taken in Atea Unified Workplace. For a complete record, use Microsoft 365 audit logs." with external link if an existing URL is defined (else text only). Nav label stays "Activity".
- Table columns: When (relative, absolute title), Person (actor display name; fall back to UPN; never object ID), Action (humanized: map known action keys such as `users.update` to "Updated user" via `humanizeActivityAction`, unknown → sentence-cased key), Target (human summary: target display name or type + "Unnamed {type}"), Result (`StatusBadge`).
- Correlation ID, target ID, actor object ID and metadata JSON move to a per-row "Details" disclosure (`TechnicalDetails` in an expandable row; compact list shows it at the bottom of each item). Remove `correlationText` from the visible cell (around line 241) and the JSON cell (around lines 210/231).
- Filters (UX-035): "Actor object ID" → "Person" (placeholder "Name, email or object ID"); action filter placeholder "e.g. Updated user" (keep accepting raw keys); advanced technical filters (correlation ID) inside a "More filters" disclosure with the active-count rule from §5.2.

### 5.9 Settings — `features/workspace-settings/WorkspaceSettingsHub.tsx`, `WorkspaceSettingsPage.tsx`, `WorkspaceModulesPage.tsx`, `OnboardingPage.tsx`, `features/workspace-access/WorkspaceAccessPage.tsx` (UX-037, UX-038, UX-039, UX-040, UX-042; UX-041 deferred)

- **Orientation (UX-037):** keep single page + hash sections (`#connection`, `#general`, `#modules`, `#access`). At ≥ 64rem: two-column layout, left a sticky section index (`nav.settings-index` exists at line 40) with `aria-current="true"` on the section in view (IntersectionObserver; fall back to hash), right the sections as cards. At < 64rem: the index becomes a horizontal scrollable chip row under the header. Each section card has H2 + one-line purpose + its own status line. On hash navigation, move focus to the section H2 (`tabIndex=-1`).
- **Dirty state (UX-038):** General and Modules forms track `isDirty` (compare to loaded values). Footer per section (sticky to card bottom on desktop): "Unsaved changes" warning-tone meta text when dirty, "Reset" (tertiary) and "Save changes" (primary, disabled when not dirty or saving). `beforeunload` warning is out of scope. After save: inline `role="status"` "Saved {relative time}".
- **Modules (UX-042):** each module is a row: name + one-line description + audience text ("Visible to: owners and members with access") + `StatusBadge` Enabled/Disabled + a switch (`role="switch"` checkbox styled) on the right. On Save, if any module changes, show a summary in a `ConfirmationDialog` (default tone): "Turn off Exchange — members will lose access to Exchange pages." / "Turn on Devices — …"; confirm "Save module changes". Keep the existing restore-grants confirmation (line ~65) — chain it after or merge its text into the same dialog (plan writer chooses the simpler of the two; behaviour of grant restoration must not change).
- **Invitation (UX-039):** form in two groups: "Person" (Email, Display name — two columns ≥ 48rem) and "Access" (Role as radio cards with one-line descriptions; Module access as a checkbox list using `.checkbox-field`). Disabled modules are not mixed in: show them separately as meta text "Exchange is turned off for this workspace" with "Open module settings" link. Submit "Send invitation" (primary).
- **Consent guidance (UX-040):** `OnboardingPage`/Connection section becomes a 3-step ordered checklist with status per step: 1) "Atea app owner grants the platform app" 2) "Customer admin approves permissions" (button/link to the existing consent URL) 3) "Activate roles with PIM when needed" ("Open PIM guidance"). Each step: title, one sentence, status badge (Done / Action needed / Unknown), one action. Long explanatory text and the scope list move into `TechnicalDetails` ("Permissions requested").
- Delete repeated "Source … Retrieved …" lines in favour of one header `DataFreshness`.

### 5.10 Identity / PIM guidance — `app/routes.tsx` `/identity` route (~line 49) → extract to `features/identity/PimGuidancePage.tsx` (UX-043, UX-044)

- Header: title "PIM guidance", description "Activate your Microsoft Entra roles to use admin actions here."
- Checklist: 1) "Open Microsoft Entra PIM" (external link with external icon and sr-only "(opens in a new tab)", `target="_blank" rel="noopener noreferrer"`) 2) "Activate the role you need" — show the roles the workspace's notifications say are missing, if available from existing issue data; else generic text listing typical roles (User Administrator, Intune Administrator) as plain text 3) "Come back and refresh access" — "Refresh access" button calling the capabilities/notifications refresh, then shows the resulting status line.
- Linked from permission states, notifications and Overview (§4.3).

### 5.11 404 — `app/routes.tsx` `WorkInProgressPage` / not-found (~lines 36–44, 137) (UX-045, UX-046)

- Eyebrow "Error 404" (replaces `shellPreviewLabel` "Preview route" for this case; keep the message key for any genuine preview routes, or remove if unused), title "This page isn't available", description "The link may be out of date, or you may not have access.", meta the attempted path in `<code>`.
- Actions: primary "Go to overview" (`/overview`), secondary "Open PIM guidance" only if a permission issue exists (optional; plan writer may skip).

### 5.12 Platform admin — `features/admin/AdminShell.tsx`, `WorkspaceListPage.tsx` (UX-047, UX-048)

- `AdminShell` line 22: "Atea platform administration" becomes a non-heading brand label (`<p class="admin-brand">` or inside header as text); "Workspaces" remains the only H1 (UX-048).
- Workspace list: primary = workspace name + status; "Tenant ID: …" (line 27) → `CopyValue truncate` in a meta line or `TechnicalDetails` (UX-047).
- Uses the new `ThemeToggle` and header styling.

## 6. Functional fixes

### 6.1 Favicon 401 (UX-002)

Root cause: no `favicon.ico` exists in `wwwroot`; `UseStaticFiles` (Program.cs ~line 139, before auth) serves real files anonymously, but `/favicon.ico` falls through. `MapFallbackToFile("index.html").AllowAnonymous()` (~line 209) does not match file-like paths, so the request reaches the authorization `FallbackPolicy` (`Authorization/PlatformAuthorization.cs` ~line 55) and returns 401.

Fix (frontend only, no auth change):
- Add `src/Web/public/favicon.svg` (Atea mark derived from the existing logo asset, monochrome-safe) and `src/Web/public/favicon.ico` (32×32, generated once from the SVG; if no tool is available, ship only the SVG and an `apple-touch-icon` is out of scope).
- `src/Web/index.html`: add `<link rel="icon" type="image/svg+xml" href="/favicon.svg">` (and `<link rel="alternate icon" href="/favicon.ico">` if the ICO exists).
- Vite copies `public/` into `dist`; the Dockerfile already copies `dist` into `wwwroot`, so `UseStaticFiles` serves both anonymously. Verify the Dockerfile copy path during implementation; do not change `FallbackPolicy`.
- Node test asserts `index.html` contains `rel="icon"` and that `public/favicon.svg` exists.

### 6.2 License friendly names (UX-023)

Extend `src/Api/Features/Licenses/LicenseDisplayNameResolver.cs` (currently SPE_E5, SPE_E3, DEVELOPERPACK_E5, ENTERPRISEPREMIUM, ENTERPRISEPACK, VISIOCLIENT) with — **each verified against Microsoft's "Product names and service plan identifiers for licensing" reference, as the file's comment requires**:
- `EMSPREMIUM` → "Enterprise Mobility + Security E5"
- `EMS` → "Enterprise Mobility + Security E3"
- `AAD_PREMIUM` → "Microsoft Entra ID P1"
- `AAD_PREMIUM_P2` → "Microsoft Entra ID P2"
- `SPB` → "Microsoft 365 Business Premium"
- `O365_BUSINESS_PREMIUM` → "Microsoft 365 Business Standard"; `FLOW_FREE` → "Microsoft Power Automate Free"; `POWER_BI_STANDARD` → "Microsoft Fabric (Free)" — only if verified; drop any that can't be verified.
Bump the mapping version comment and add/extend API unit tests for the new entries. No contract change (the `displayName` field already exists).

### 6.3 Bugs found in the inspection

| Bug | Fix | Section |
|---|---|---|
| Sidebar background ends ~490px | Surface on sticky full-height container | §4.3 |
| Checkbox rendered centred above its label (dialogs, invite form) | `.checkbox-field` inline layout; `input[type=checkbox|radio] { width:auto }` | §3.9, §5.9 |
| Group name + nickname concatenated ("haugentechhaugentech") | Stacked layout, nickname only if different | §5.3 |
| Role name + state concatenated ("Global Readeractive") | `StatusBadge` + humanized state | §5.3 |
| UPN/email broken mid-word in tables | Remove global `overflow-wrap:anywhere`; ellipsis + `title` | §3.8 |
| Overview "Devices: Unavailable" while Devices page lists devices | Navigational Devices card (no count in overview API) | §5.1 |
| License numbers misaligned with headers | `numeric` class on `th` and `td` | §3.8, §5.4 |
| Device GUID shown as breadcrumb | Generic "Details" ID crumb | §4.5 |
| Notification badge pink/low contrast in dark mode | Warning status tokens | §4.1 |
| Theme toggle styled as primary button | Icon switch | §4.2 |

## 7. Accessibility and responsive requirements

Target: WCAG 2.2 AA intent. These are acceptance requirements for every touched surface.

- **Focus (2.4.7, 2.4.11):** all interactive elements use `--uw-focus-ring` via `:focus-visible`; focus is never hidden behind the sticky header (`scroll-padding-top: var(--uw-header-height)` on `html`).
- **Contrast (1.4.3, 1.4.11):** status `-fg` on `-bg` ≥ 4.5:1, `-border`/icons ≥ 3:1 against `--uw-surface`, in both themes. Muted text (`--uw-muted`) ≥ 4.5:1 on `--uw-surface` and `--uw-canvas`. Record the measured pairs in the PR description.
- **Not colour alone (1.4.1):** every tone carries a text label; danger groups also carry a heading "Danger zone".
- **Reflow (1.4.10) / 200% zoom:** no page-level horizontal scroll at 320 CSS px width (except inside table scroll regions, which are focusable and labelled). Shell, page header, toolbars, dialogs and forms reflow. Compact list at ≤ 56rem.
- **Target size (2.5.8):** interactive targets ≥ 24×24 CSS px; icon buttons 40×40.
- **Landmarks and headings (1.3.1, 2.4.6):** one H1 per page (`#page-title`), `main` labelled by it, no page-level `section aria-label` duplicates; card titles H2, groups H3. Admin has one H1.
- **Dialogs:** `role="dialog"` + `aria-modal`, labelled by title, described by consequence; focus contained; Escape closes unless busy; focus returns to trigger; initial focus never on a destructive confirm.
- **Disclosures:** `TechnicalDetails` uses native `<details>/<summary>`; `ActionMenu` keeps its existing keyboard model (verify Arrow/Escape/Home/End and that disabled items expose `aria-disabled` plus their description).
- **Live regions:** `DataFreshness` (`role=status`) must not re-announce on every render — only when the label changes. `CopyValue` "Copied" via a polite region. Save confirmations `role=status`; failures `role=alert`.
- **Switches:** theme toggle and module switches expose `role="switch"` + `aria-checked` and a stable name.
- **External links:** external icon + sr-only "(opens in a new tab)".
- **Motion:** spinners and transitions respect `prefers-reduced-motion: reduce`.
- **Viewports to check manually:** 1440×900 light/dark, 1024×768, 390×844 light/dark, 1440 at 200% zoom.

## 8. Testing strategy

Commands: `cd src/Web && npm test` (node `--test`, `tests/Web.UnitTests/*.mjs`), `npm run test:behavior` (vitest + jsdom + testing-library, `tests/Web.UnitTests/**/*.test.tsx`), `npm run build` (tsc + vite); API: `dotnet test` for the API test project covering `LicenseDisplayNameResolver`. Verify the exact paths in `package.json` and the solution before writing the plan.

### 8.1 Existing tests to update (copy changes are intentional; update assertions, do not delete coverage)

| Test | Change |
|---|---|
| `tests/Web.UnitTests/theme-and-catalog.test.mjs` | Must keep passing unchanged: `theme.css` still contains `--uw-primary`, `:root[data-theme='dark']`, `.button--primary`, `.page-action-bar`, `.detail-card`; `ThemeToggle` still contains `role="switch"`. Extend (optional) to assert `foundations.css`/`components.css` are imported in `main.tsx`. |
| `ThemeToggle.test.tsx` | Name "Enable/Disable dark mode" → switch named "Dark mode" with `aria-checked` false/true. |
| `AppShell.test.tsx` | Breadcrumbs assertions stay; add device-ID crumb "Details"; assert header no longer renders "Access snapshot". |
| `UserMutationDialogs`, `UserSecurityDialogs`, `AuthenticationMethodsSection`, `UsersPage`, `UserDetailPage`, `DevicesPage` tests, E2E `tests/Web.E2E/user-lifecycle.spec.tsx` | "Confirm action" → action-specific confirm labels; "Required capability: users.update" → "Requires permission: Edit users". |
| `UsersPage` / `UsersTable` tests | "More details for…" and "Open" buttons removed → name link and "Actions for {name}" menu. |
| `LicensesPage.test.tsx` | "Product name unavailable" → part number shown as name + "Unrecognised product". |
| Any test asserting `DataFreshness` banner text ("Directory data is fresh") | → quiet status line text ("Up to date"). |

### 8.2 New tests

- `components/WorkspaceDataState.test.tsx`: each kind renders the right role, title/message, `affected` list, Retry calls `onRetry` and is disabled while `retrying`; legacy `state` prop still works.
- `components/DataFreshness.test.tsx`: fresh → `role=status`, no banner class, relative text and absolute `title`; stale → warning label; `onRefresh` button.
- `components/TechnicalDetails.test.tsx` / `CopyValue.test.tsx`: collapsed by default, empty items omitted, renders nothing when empty; truncation + full value accessible; copy calls `navigator.clipboard.writeText` and announces "Copied"; button hidden without clipboard.
- `components/MetricCard.test.tsx`: linked card name; null value renders "—" + reason, never "Unavailable" as the value.
- `components/ConfirmationDialog.test.tsx` (extend): `confirmLabel`, `consequence`, danger tone class, humanized capability, raw key only inside technical details, checkbox inline label association.
- `format/dateTime.test.ts`, `format/humanize.test.ts`: relative-time thresholds with injected `now`; every `Capability` member mapped (iterate the union/const list); auth method mapping incl. unknown; assignment state mapping.
- `TenantContextHeader`/`NotificationsMenu`: no pill when healthy; "Access limited" chip when degraded opens panel; panel shows "checked … ago" + Refresh.
- `DeviceDetailPage.test.tsx`: GUIDs absent from overview card and present in Technical details; routine vs danger groups; Wipe confirm button text "Wipe device", typed phrase required.
- `OverviewPage.test.tsx`: metric cards link to routes; no hero "Unavailable"; needs-attention items have actions.
- `App`/module-disabled test: "Open module settings" when `canManageModules`, otherwise only "Back to overview".
- 404 test: "This page isn't available", "Go to overview" link, no "Preview route".
- Settings: Save disabled until dirty; "Unsaved changes" appears; Reset restores; module change summary dialog lists changes.
- `AuditActivityPage.test.tsx`: correlation ID/JSON not in visible cells, available in row details; "Person" filter label.
- Node test: `src/Web/index.html` has `rel="icon"`; `src/Web/public/favicon.svg` exists.
- API: resolver unit tests for each new SKU mapping.

### 8.3 Manual verification

Run the app (mock/demo mode if available; otherwise against a dev tenant) and repeat the inspection screenshots list from `docs/ux-inspection/inspection-plan.md` for the viewports in §7; attach before/after for Overview, Users, User detail, Licenses, Devices, Device detail, Settings, mobile nav and dark mode to the PR.

## 9. Traceability

Legend: **In** = resolved in this PR; **Partial** = in scope with a named part deferred; **Deferred** = not in this PR.

| ID | Sev | Finding (short) | Status | Resolved by |
|---|---|---|---|---|
| UX-001 | M | Header metadata competes with task content | In | §4.1 |
| UX-002 | L | Favicon 401 | In | §6.1 |
| UX-003 | H | Permission failures fragmented | Partial | §3.4 permission kind, §4.1 chip + notifications, §5.1/5.3/5.6/5.7 single next step. Deferred: dedicated permission-health dashboard (notifications panel serves as the single health model) |
| UX-004 | M | Inconsistent status vocabulary | In | §3.3 |
| UX-005 | M | Landmarks not consistently named | In | §3.6, §7 |
| UX-006 | H | Raw implementation terms in actions | In | §3.5 rule 3, §3.9, §5.3 |
| UX-007 | H | Destructive and routine actions same weight | In | §3.2, §3.10, §5.3, §5.5, §5.6 |
| UX-008 | L | Account area looks like controls | In | §4.1 |
| UX-009 | L | Dark-mode label describes action | In | §4.2 |
| UX-010 | M | Header status changes across navigation | In | §4.1 |
| UX-011 | M | Overview cards not actionable | In | §3.7, §5.1 |
| UX-012 | H | "Devices Unavailable" hero metric | In | §3.7, §5.1 |
| UX-013 | L | Technical freshness copy | In | §3.4, §5.1 |
| UX-014 | M | Needs-attention not linked | In | §5.1 |
| UX-015 | M | Principal names dense primary content | In | §3.8, §5.2 |
| UX-016 | M | Repetitive More/Open/Disable actions | In | §3.8, §5.2 |
| UX-017 | M | Hidden filters in "More filters" | In | §5.2 |
| UX-018 | H | "Partial user details" unclear | In | §5.3 |
| UX-019 | M | User detail vertical space | Partial | §5.3 compression + two-column grid. Deferred: sticky in-page section nav |
| UX-020 | H | Auth method class names | In | §3.5 rule 4, §5.3 |
| UX-021 | M | No per-section retry | In | §3.4, §5.3 |
| UX-022 | H | Capability-centric dialogs | In | §3.9, §5.3, §5.6 |
| UX-023 | H | Raw SKU names and GUIDs | In | §5.4, §6.2 |
| UX-024 | M | Assigned-users tab lacks selector | In | §5.4 |
| UX-025 | L | Seat metrics not explained | In | §5.4 |
| UX-026 | M | Device GUID under name | In | §5.5 |
| UX-027 | H | Device detail raw IDs as facts | In | §5.6 |
| UX-028 | H | Recovery repeats scopes/jargon | In | §5.6 |
| UX-029 | C | Remote actions lack safety framing | In | §3.9, §3.10, §5.5, §5.6 |
| UX-030 | L | Inconsistent date presentation | In | §3.4 date formatting, §5.5, §5.6 |
| UX-031 | M | Disabled recovery controls lack next action | In | §5.6 |
| UX-032 | H | Disabled module route lacks recovery | In | §5.7 |
| UX-033 | M | Exchange hidden in nav but reachable | In | §4.3 (stays hidden by design), §5.7 consistent unavailable-route pattern |
| UX-034 | H | Audit raw correlation IDs/JSON | In | §5.8 |
| UX-035 | M | Activity filter internal language | In | §5.8 |
| UX-036 | M | Audit vs platform log boundary | In | §5.8 |
| UX-037 | H | Settings long and scan-heavy | In | §5.9 |
| UX-038 | H | No dirty-state feedback | In | §5.9 |
| UX-039 | M | Invitation form layout | In | §5.9, §3.9 checkbox fix |
| UX-040 | M | Consent guidance long/repetitive | In | §5.9 |
| UX-041 | M | Legacy routes → hash sections | **Deferred** | Changing to canonical `/settings/{section}` routes alters the routing model and redirect contract from the previous spec; this PR only focuses the section heading on hash navigation (§5.9). Revisit with routing work |
| UX-042 | M | Module checkboxes without impact | In | §5.9 |
| UX-043 | M | PIM guidance lacks return/check | In | §5.10 |
| UX-044 | L | Identity not in nav | In | §4.3, §5.10 (linked from remediation surfaces, not nav) |
| UX-045 | M | 404 no recovery button | In | §5.11 |
| UX-046 | L | 404 branded "Preview route" | In | §5.11 |
| UX-047 | M | Admin tenant GUID as primary | In | §5.12 |
| UX-048 | M | Admin duplicate H1 | In | §5.12 |
| UX-049 | H | Technical IDs as IA | In | §3.5 |
| UX-050 | H | No shared state component | In | §3.4 |
| UX-051 | M | Dark mode status semantics | In | §3.1, §3.3, §4.1, §7 |
| UX-052 | L | Inconsistent action labels | In | §3.5 rule 7, §4.5 |
| UX-053 | L | No density mode | **Deferred** | Requires a new persisted user preference; this PR tightens default row height (§3.8). Revisit if admins ask for it |
| UX-054 | H | 200% zoom overflow on Users | In | §3.8, §5.2, §7 |

**Counts:** 54 findings — 52 in scope (50 fully, 2 partial: UX-003, UX-019), 2 deferred (UX-041, UX-053). All 18 Critical/High findings are in scope.

### Suggested implementation order (for the plan writer)

1. **Foundations:** `foundations.css`, `components.css`, imports, button variants, status tokens, focus ring, `icons.tsx`, `format/dateTime.ts`, `format/humanize.ts` (+ tests).
2. **Shared components:** `WorkspaceDataState`, `DataFreshness`, `TechnicalDetails`, `CopyValue`, `MetricCard`, `ActionGroup`, `StatusBadge`, `WorkspacePageHeader`, `ConfirmationDialog`, `ActionMenu` description (+ tests).
3. **Shell:** header, theme toggle, notifications, sidebar, mobile header/drawer, breadcrumbs, landmarks.
4. **Critical/High pages:** Device detail (UX-029 first), Devices, User detail, Users (+ zoom), Licenses, Activity, Settings, module-disabled route.
5. **Medium/Low pages:** Overview, Identity, 404, Admin, invitation form, consent checklist, module impact summary.
6. **Functional:** favicon, SKU resolver + API tests.
7. **Verification:** update existing tests, full test/build run, manual a11y/viewport pass, screenshots for the PR.

If time runs short, cut in this order (last first): UX-042 impact dialog, UX-040 checklist statuses, Overview shortcuts card, Activity action humanization beyond known keys — never cut steps 1–4.

## 10. Assumptions and open questions

Decisions made on the user's behalf (autopilot):

1. **Approach B** (foundations + evolved shared components); no UI library, icons are local inline SVG.
2. **Separate CSS files** added after `theme.css` rather than refactoring the 2,935-line stylesheet; superseded rules are deleted only where replaced.
3. **No account menu / sign-out** added; the account area is informational.
4. **Disabled modules stay hidden** from navigation (server authority, previous spec); direct URLs get a consistent unavailable pattern with a next step.
5. **Identity/PIM guidance is not added to the nav**; it is linked from every remediation surface.
6. **Breadcrumbs are kept** (tested) and complemented by page-header back links; hidden on mobile.
7. **Overview gets no device count**; the API contract is unchanged, so the Devices card is navigational.
8. **No reason field for device remote actions**; the API has none and contracts are unchanged. Safety comes from grouping, consequence copy and typed confirmation.
9. **Settings keeps hash sections** (UX-041 deferred).
10. **Density toggle deferred** (UX-053); default rows are tighter.
11. **Relative time** uses `Intl.RelativeTimeFormat`/`DateTimeFormat` with the browser locale (UI copy remains English).
12. **SKU friendly names** are only added after verification against Microsoft's licensing reference; unverifiable entries are dropped.
13. **Favicon fixed without touching authorization** — a static file served by `UseStaticFiles` before auth.
14. **Detail pages have no primary button**; actions are secondary with a "More actions" menu and a danger zone.
15. **Confirmation for Restart and Remote lock** uses default tone (still confirmed); Retire/Wipe use danger tone + typed phrase (existing).
16. Existing **safety confirmations, routes, API contracts and authorization boundaries are unchanged**; no new Microsoft 365 operations.

Open questions for the user (do not block this PR):

- Should owners see disabled modules in the nav with a "Turned off" label instead of hidden items?
- Is a per-user table density preference wanted (UX-053)?
- Should settings move to canonical routes `/settings/{section}` (UX-041)?
- Is a dedicated "Access health" page wanted beyond the notifications panel (rest of UX-003)?
- Should Microsoft 365 audit log deep links be added to the Activity scope note (needs a confirmed URL per tenant)?
