# Task 6 acceptance report

## Baseline and master integration

- Rebased the approved Task 1–5 work onto fetched `origin/master` (`38b552f`, including F1 #11 and F5 #13).
- `ACCEPTANCE_BASE=eb536b72c594ab427e4cb10b37e617ffb9385d2d` (post-rebase HEAD immediately before acceptance edits).
- Rebase replayed nine commits. `src/Web/src/messages/en.ts` conflicted while applying the directory-view commit. Resolved additively by retaining both `authenticationCampaignMessages` and `userFeatureMessages` imports and spreading both catalogs. `tests/Web.UnitTests/theme-and-catalog.test.mjs` auto-merged; no conflict edits were needed there.
- New HEAD: the Task 6 acceptance commit containing this report; its object ID is reported in the final handoff after commit creation.

## Acceptance changes

- Added E2E contracts for directory shortcut URL preservation, unsupported-parameter removal, paging reset, and back/forward filter restoration.
- Added reason JSON assertions for directory create/disable and profile disable; added User 360 tab ArrowRight/Home/End and explicit unavailable Activity/phishing/sign-in assertions without positive MFA-compliance claims.
- Updated the existing local User 360 fixture to open its Devices tab before asserting its associated-device link.
- Added the required safe TAP policy/default/range/reason guidance to the current local runbook and updated the F2 Users inventory entries without changing sibling rows.
- Fixed the new TAP result date display to use the shared date formatter after the full Node contract suite identified its `toLocaleString` violation.
- Added the required reason to two integration-test request fixtures; this preserves the new write-body contract while allowing replay and cross-tenant assertions to reach their intended behavior.

No schema, scope, W0, authorization, source, sibling-feature, or deployed-service changes were made.

## Verification

All commands were run from the repository worktree. API commands used:

```bash
export DOTNET_ROOT=/Users/sondre.haugen/.copilot/session-state/5b6768d3-837e-4726-92b3-50f35edce141/files/dotnet
export PATH="$DOTNET_ROOT:$PATH"
```

| Command | Result |
| --- | --- |
| `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` | Passed: 471, failed: 0, skipped: 0 |
| `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` | Passed: 205, failed: 0, skipped: 1 (`RealEntraValidationTests`, intentionally requires a real tenant) |
| `cd src/Web && npm run test:behavior -- --run` | Passed: 73 files, 543 tests |
| `cd src/Web && npm run build` | Passed TypeScript and Vite production build; emitted existing non-blocking dynamic-import and bundle-size warnings |
| `npm test --prefix tests/Web.UnitTests` | Passed: 19 Node contracts |
| `npm test --prefix tests/Web.E2E` | Passed: 12 files, 42 tests; 6 Node smoke/local-compose contracts passed |
| `git diff --check` | Passed |

The first integration run exposed missing reasons in the replay/cross-tenant test request fixtures; those fixtures were corrected, and the full suite passed on rerun. One concurrent suite attempt also hit a transient PostgreSQL SSL startup error; the sequential full rerun passed.

## Browser verification

Loaded the real User 360 and directory components in a bounded local Vite fixture with a deterministic same-origin API mock. The fixture rejects cross-origin API calls and does not add an authentication bypass to production code. No Entra tenant, Prism, secrets, or live API was used.

- Desktop, narrow (375 CSS px), and 2× page-scale/reflow checks showed no horizontal overflow (document width matched the viewport).
- Verified long identity/device values, keyboard ArrowRight/Home/End tab behavior, explicit unavailable Activity and section state, keyboard-visible focus styling, reason validation announcement/focus, same-origin reason JSON, and focus return to the action trigger.
- A requested 1,001-character reason announced the 1,000-character limit and focused the reason field; a valid reason was then submitted as JSON.
- Evidence is in session artifacts:
  - `files/task6-browser/users-directory-desktop.png`
  - `files/task6-browser/users-directory-narrow.png`
  - `files/task6-browser/users-directory-zoom.png`
  - `files/task6-browser/user360-desktop.png`
  - `files/task6-browser/user360-narrow.png`
  - `files/task6-browser/user360-page-scale-2.png`
  - `files/task6-browser/user360-zoom-reflow-600css.png`
  - `files/task6-browser/user360-reason-validation.png`

The fixture server was stopped after verification.
