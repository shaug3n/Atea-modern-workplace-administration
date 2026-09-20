# Atea UI Usage

Task 7 establishes the Atea-branded application shell for the Unified Workplace MVP.

## Assets and Provenance

The shell uses the personal Atea webapp design skill snapshot from 8 September 2026:

- `src/Web/src/styles/atea-tokens.css` is copied from the skill package and imports the bundled Inter variable font files beside it.
- `src/Web/src/styles/fonts/InterVariable.woff2`, `InterVariable-Italic.woff2`, and `LICENSE.txt` are copied intact.
- `src/Web/src/assets/logos/atea-logo-grey.svg` and `atea-logo-white.svg` are copied from the supplied original SVGs. The shell renders one logo image and swaps between the grey and white original assets by theme.

No gradients or redrawn logo artwork are used.

## Token Mapping

`src/Web/src/styles/theme.css` imports the Atea token sheet once and maps it into application aliases prefixed with `--uw-*`.

Light mode uses the supplied neutral Atea surfaces, grey text tokens, teal links, green primary action tokens, and semantic success, warning, danger, and information pairs from the token sheet.

Dark mode defines explicit semantic mappings for:

- canvas, surface and subtle surface
- heading, body and muted text
- border and focus treatment
- links and primary actions
- success, warning, danger and information status badges

The app sets `document.documentElement.dataset.theme` to `light` or `dark`; components consume semantic tokens rather than branching their own visual implementations.

## Theme Preference

`ThemeProvider` uses a `light | dark` preference. It falls back to the system colour scheme only before a saved user preference exists. In production it calls `/api/user-preferences/theme` through the existing authenticated API boundary and writes only `{ "theme": "light" | "dark" }`. The API stores the preference by verified tenant ID and user object ID claims, without requiring workspace membership. If the endpoint is unavailable during local development, the frontend falls back to in-memory storage for the theme value only. No tenant credentials or Microsoft Graph tokens are stored by the preference layer.

## Shell and Navigation

The app shell provides:

- skip-to-content link
- semantic header, primary navigation and main landmarks
- Atea home link to `/overview`
- current workspace name and signed-in user
- connection freshness badge with readable text
- keyboard-accessible theme switch

Navigation is capability-aware. It consumes the existing `/api/capabilities` contract and hides Users when `users.view` is not allowed, Licenses when `licenses.assign` is not allowed, and Workspace settings when `workspace.settings.manage` is not allowed. Direct access to guarded routes still renders an explicit permission state.

The Task 7 brief used the name `workspace.configure`, but the completed backend contract at `eb9ff3c` exposes `workspace.settings.manage`. The shell follows the existing contract to avoid a second permission system.

## Accessibility Decisions

The shell keeps visible focus outlines on links, buttons and focusable regions. Navigation active state uses `aria-current="page"` and a non-colour indicator. Status badges include text and do not rely on colour alone. The narrow layout stacks the header context, keeps navigation reachable as a horizontal labelled nav, and uses 16px page padding. Reduced-motion users receive no decorative transitions.

Known follow-up: the repo did not include Playwright, a Playwright config, or `@playwright/test`, so browser viewport smoke coverage could not be executed in Task 7.
