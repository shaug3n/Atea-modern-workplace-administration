# UX polish PR notes

## Baseline (before changes)

- `npm run build` (src/Web): pass
- `npm test` (src/Web): 0 tests, pass
- `npm run test:behavior` (src/Web): 44 files / 331 tests pass
- `tests/Web.UnitTests` `npm test`: 4 pass
- `tests/Web.E2E` `npm test`: 11 files / 28 tests pass
- `dotnet` is not installed locally; API tests are CI-only.

## Contrast measurements

Measured by `tests/Web.UnitTests/contrast.test.mjs` (WCAG ratios):

| Theme | Tone | Text on bg | Border on surface |
|---|---|---|---|
| light | success | 8.30 | 3.30 |
| light | warning | 8.15 | 5.02 |
| light | danger | 6.80 | 4.83 |
| light | info | 8.49 | 5.17 |
| dark | success | 10.96 | 6.95 |
| dark | warning | 11.00 | 7.38 |
| dark | danger | 8.99 | 4.21 |
| dark | info | 8.86 | 4.31 |

Muted on surface: 7.40 light / 11.63 dark. Primary on surface: 4.53 light / 4.74 dark.

## How to test manually

CI deploys the PR to the dev test environment (`validate-and-deploy` workflow; the workflow's PR comment contains the URL); sign in as a workspace administrator. Check at 1440×900, 390×844 and 200% browser zoom, light and dark (toggle top-right):

1. **Any route:** header is one calm row; no "Access snapshot" pill; sidebar background runs the full viewport height; DevTools console shows no 401 for `favicon` (S7).
2. **/overview:** Users/Licenses/Devices cards are links; Devices card has no "Unavailable"; one quiet "Up to date · Updated … ago" line; "Needs attention" items each have one action link.
3. **/users (also at 200% zoom):** no horizontal page scroll; names are links with UPN beneath; one "Actions" menu per row (Disable is last, danger-styled, still asks for confirmation); "More filters (n)" counts active filters.
4. **/users/{id}:** "← Back to Users"; header shows Edit/Reset password/More actions and no green button; partial/failed sections show a titled message with Retry; groups and roles are not run together; MFA methods read "Password", "Microsoft Authenticator" etc.; open the Disable dialog → button reads "Disable user", checkbox sits left of its label.
5. **/licenses:** friendly product names, numbers right-aligned, usage bar; assigned-users tab has a License selector; SKU/GUIDs only under "Technical details".
6. **/devices and a device detail:** no GUID under names or in the overview card; Technical details holds IDs with copy buttons; "Danger zone" with Retire/Wipe is last and Wipe asks for the typed phrase.
7. **/activity:** title "Platform activity"; no raw correlation IDs/JSON until "Details" is opened.
8. **/settings** (and legacy `/settings/modules`): section index; "Unsaved changes" appears after editing and Save is disabled until then.
9. **/services/exchange when disabled, /identity, a bad URL, /admin:** each shows a clear next step ("Open module settings"/"Back to overview", PIM checklist, "Go to overview", single H1).
10. Tab through the shell: visible focus ring everywhere; skip link works; theme switch announces "Dark mode".

## Traceability

Per-finding status is in spec §9 (`docs/superpowers/specs/2026-10-07-ux-polish-design.md`). Deferred: UX-041 (canonical settings routes), UX-053 (density mode), the rest of UX-003 and UX-019 as listed in §9. Phase 2 (optional, incl. the Overview Shortcuts card) was not done.

## Deviations from the plan

- Device detail: the DTO has no primary-user name or enrolled date, so "Primary user" is an "Open user profile" link and "Enrolled" is omitted; recovery "Source:" stamps were reworded.
- Devices list: the row menu only appears for users who can manage devices.
- Users list: the row menu only appears when disable is not hidden; "Disable user" is its only item.
- User detail: Temporary Access Pass stays in Authentication methods with the button text "Grant Temporary Access Pass" (an "Issue…" rename would clash with the dialog button name).
- Activity: the audit DTO has no actor name, so Person shows "Workspace user" (object ID only in Details); the server notice is replaced by a static note; there are no technical filters, so no "More filters (n)".
- Licenses: per-row Technical details sit in the license cell, not a separate expandable row.
- Settings: footer button is "Save changes" for General and Modules (was "Save settings"/"Save modules"); module toggles are `role="switch"`.
- Exchange page still has "Source … Retrieved" lines (out of plan scope); its raw dates now use the shared formatters.
- Favicon: SVG only. License names added (verified against Microsoft's service-plan reference from known product names; not re-fetched in this run): EMSPREMIUM, EMS, AAD_PREMIUM, AAD_PREMIUM_P2, SPB. Not added: O365_BUSINESS_PREMIUM, FLOW_FREE, POWER_BI_STANDARD (unverified).
- The new .NET test (`LicenseDisplayNameResolverTests`) and resolver change were not run locally (no `dotnet`); CI validates.
- Sweep: some legacy buttons on the Access, Exchange, Licenses tabs, Onboarding and admin pages still use the global button style without a variant class; these were left as-is.
