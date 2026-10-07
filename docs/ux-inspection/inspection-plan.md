# Visual UX Inspection Plan — Atea Unified Workplace (dev)

**Date:** 2026-10-07
**Target:** https://atea-workplace-dev.thankfulbay-7a5080b4.norwayeast.azurecontainerapps.io/
**Tooling:** Playwright MCP (`playwright-browser_*`) — primary. Computer-use only as fallback if Playwright cannot reach a surface.
**Executor model:** cost-efficient model (GPT-5.6 Luna) following this plan step by step.
**Output:** `docs/ux-inspection/report.md` + screenshots in `docs/ux-inspection/screenshots/`.

## Goal

Produce an evidence-based report of design and functionality problems that stop the app from feeling professional, UX-friendly and easy to use. The report is handed to a brainstorming agent that designs the improvements, so every finding must be concrete, located (route + element), reproducible, evidenced (screenshot file), and severity-rated. The report describes problems and observations; it may suggest direction, but it must not prescribe a full design.

## Hard rules (safety on a live tenant)

1. **Read-only.** The site is connected to a real Microsoft 365 tenant. Never submit any form or confirm any dialog that changes data: no create/edit user, password reset, TAP, revoke sessions, license assign/remove, group membership, device actions, PIM activation, settings save, module toggles, member/invitation changes, consent grants, workspace creation.
2. Opening a dialog to inspect it is allowed; close it via **Cancel / Close / Escape**, never the primary button.
3. Theme toggle, navigation, search/filter inputs, sorting, pagination, expanding rows, and opening menus are allowed (theme preference is user-scoped and harmless — restore the original theme at the end).
4. Do not copy personal data into the report text (use "user row 1" rather than names/e-mails).
5. If redirected to `login.microsoftonline.com` or any sign-in page, **stop and ask the user to sign in** in the Playwright browser window. Never type credentials.
6. Do not modify application source code during the inspection.

## Setup

1. `playwright-browser_navigate` to the target URL. If it lands on Microsoft sign-in → pause for the user.
2. Screenshots must be saved inside the repo workspace (Playwright MCP only allows the workspace root): `docs/ux-inspection/screenshots/<NN>-<route>-<viewport>-<theme>.png`.
3. Record: role/workspace shown in header (no PII), theme, which nav entries are visible (capability-dependent).
4. Capture `playwright-browser_console_messages` (errors/warnings) and `playwright-browser_network_requests` once per page; note failed (4xx/5xx) API calls.

## Viewports

| Key | Size | Use |
|---|---|---|
| `desktop` | 1440×900 | Primary pass, every page, light + dark |
| `laptop` | 1280×720 | Spot-check density/overflow on table pages |
| `tablet` | 820×1180 | Shell + Users + Devices + Settings |
| `mobile` | 390×844 | Shell/nav menu + every top-level page |

Use `playwright-browser_resize`. Check horizontal overflow on each page with
`() => ({ scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth })`.

## Routes to inspect (customer workspace)

In this order (from `src/Web/src/app/routes.tsx`):

1. `/overview`
2. `/users` → open first user → `/users/:userId` (all sections: Identity, Job information, Authentication methods, Groups, Licenses, Roles & PIM, Associated devices)
3. `/licenses`
4. `/devices` → open first device → `/devices/:id`
5. `/services/exchange`
6. `/activity` (and `/audit` alias)
7. `/settings` (Connection, General, Modules, Access sections) + legacy deep links `/settings/general`, `/workspace-settings`, `/workspace-access`, `/onboarding`
8. `/identity` (PIM guidance)
9. Unknown route, e.g. `/does-not-exist` (404 state)
10. Header controls: Notifications dropdown, theme toggle, user/account menu, skip link (press Tab on load)
11. Dialogs (open, inspect, cancel): Create user, Edit user, Password reset, TAP, Revoke sessions, License assignment, Group membership, PIM activation — only those reachable and enabled.

Platform admin console `/admin` (separate login): visit once. If it requires a different sign-in, ask the user; if not authorised, document the sign-in/denied state only.

## Per-page checklist

For each page, at `desktop` light first, then dark, then the other viewports:

**A. Capture** — full-page screenshot (`fullPage: true`); accessibility snapshot (`playwright-browser_snapshot`) to verify headings, landmarks, labels, button names.

**B. First impression (5-second test)** — Is it obvious what the page is for and what the primary action is? Does it look like a polished enterprise product?

**C. Visual design**
- Hierarchy: one clear H1, sensible heading order, section grouping.
- Spacing & alignment: consistent rhythm, aligned edges, no cramped or orphaned elements.
- Typography: size scale, line length, weights, truncation, tabular numbers.
- Colour & contrast: brand use, status colours, text contrast (spot-check with `getComputedStyle`), dark-mode parity.
- Components: consistency of buttons, inputs, badges, cards, tables, dialogs across pages; icon usage; empty/loading/error visuals.
- Branding: Atea logo placement, restraint, professional feel.

**D. Usability & functionality**
- Navigation: active state, grouping (People/Services), breadcrumbs/back links, deep links, browser back.
- Tables: column choice, sorting, search/filter placement and behaviour, pagination, row click targets, row-action discoverability, long values.
- Feedback: loading indicators, skeletons, success/error messaging, data freshness, retry affordances.
- Forms/dialogs: labels, required markers, validation, button order, focus trap, Escape, focus return.
- Error/permission states: clarity, actionable next step, no jargon (Graph/scope codes) for end users.
- Copy: tone, terminology consistency (Workspace Settings vs Settings, Activity vs Audit), capitalisation, typos, raw codes/IDs.
- Perceived performance: time to meaningful content (note > ~2 s), layout shift, "Loading…" flicker.

**E. Accessibility (quick pass)** — Tab through header + page; visible focus; skip link; keyboard-operable menus/dialogs; landmarks & names; alt text; icon-only buttons named; 200% zoom check on `/users` and `/settings`.

**F. Responsiveness** — horizontal overflow, mobile nav menu, tables → list transformation, touch targets ≥ 40px, sticky elements covering content.

## Finding format

```
### UX-<NNN> — <short title>
- **Area:** <route / component>
- **Viewport/theme:** desktop-light | mobile-dark | all …
- **Severity:** Critical | High | Medium | Low | Polish
- **Category:** Visual | Layout | Navigation | Data/Tables | Forms | Feedback/States | Copy | Accessibility | Responsive | Performance | Bug
- **Evidence:** screenshots/<file>.png (+ console/network note)
- **Observed:** what happens / what it looks like
- **Expected / why it matters:** the professional/UX standard it misses
- **Suggested direction:** optional, 1–2 lines; no full design
- **Likely source:** src/Web/src/... if obvious (optional)
```

Severity: **Critical** blocks a task or looks broken; **High** confuses users or looks clearly unprofessional on a main path; **Medium** noticeable friction/inconsistency; **Low** minor; **Polish** refinement.

## Report structure (`docs/ux-inspection/report.md`)

1. Executive summary — overall impression, top 10 issues, maturity score 1–5 for Visual, Usability, Consistency, Accessibility, Responsive.
2. Environment — date, URL, role/workspace (no PII), visible modules, viewports, themes, limitations.
3. Global/shell findings.
4. Per-page findings (one subsection per route above) with screenshot references.
5. Cross-cutting patterns.
6. Console/network errors table.
7. Accessibility summary.
8. Responsive summary (overflow table page × viewport).
9. Prioritised backlog table: ID, title, severity, effort (S/M/L), area.
10. Open questions for the brainstorming agent.

## Hand-off pipeline

| Stage | Model / effort | Input | Output |
|---|---|---|---|
| 1. Inspection (this plan) | GPT-5.6 Luna | this plan | `docs/ux-inspection/report.md` + screenshots |
| 2. Brainstorming / design | Claude Opus 5.5 | report + `docs/superpowers/specs/2026-09-25-workspace-ux-redesign-design.md` + `docs/design/atea-ui-usage.md` | `docs/superpowers/specs/2026-10-07-ux-polish-design.md` |
| 3. Implementation plan | Claude Sonnet 5.5, high reasoning | design spec | `docs/superpowers/plans/2026-10-07-ux-polish.md` |
| 4. Execution | Claude Sonnet 5.5, medium reasoning | plan | code + passing `npm run build`, `npm test`, `npm run test:behavior` |
| 5. Delivery | — | — | commit + PR for user testing |
