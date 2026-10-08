# F2 Task 3: Web write reasons

## Result

Implemented required client-side write reasons for the F2 user directory and User 360 write entry points. Every submitted reason is trimmed, must be nonblank, and is limited to 1,000 UTF-16 code units. Simple confirmations use the existing `ReasonDialog`; create, edit, group, license, and TAP forms use the controlled feature-owned `UserWriteReasonField`.

Create/edit/group/license payloads retain their existing fields while adding `reason`. Disable, reactivate, password reset, session revoke, authentication-method remove/reset, and TAP requests now send JSON reason bodies. Group and license `DELETE` requests serialize provided bodies, while bodyless `DELETE` remains supported. Existing idempotency headers remain on all direct write helpers.

Successful audit warnings remain visible after dialog closure through the owning page or section. Temporary passwords and TAP secrets remain confined to their existing one-time result displays; no reason or secret is placed in URLs, browser storage, or telemetry. Existing confirmation phrases, double-submit guards, focus handling, auth-read retries, and summary states are retained. No W0 component, API contract, or TAP options work was changed.

## Test-first evidence

- Baseline on `86cc92d`: the specified five behavior suites passed, 93/93 tests.
- RED: newly added assertions for a controlled reason field and reason-bearing request payloads failed against the pre-implementation UI.
- GREEN: the exact approved command passed, 103/103 tests across five files.
- Full web behavior suite passed, 512/512 tests across 71 files.
- `npm run build` passed (`tsc -b` and Vite production build).
- `git diff --check` passed.

The production build continues to print Vite warnings about the existing `AuthProvider` dynamic/static import overlap and a bundle chunk exceeding 500 kB; neither is in the changed files.

## Scope and follow-up

Changes are limited to the F2 users feature, its focused Web.UnitTests, and this task report. Web requests now carry the reason JSON expected by the later API-contract task; this task intentionally does not modify or validate the API implementation.
