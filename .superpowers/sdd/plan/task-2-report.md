# F2 Task 2: User 360 Profile Workspace

## Outcome

Implemented the User 360 workspace with accessible Identity, Devices, and Activity tabs; honest summary states; capability/module-aware profile actions; card-level editing through the existing editor; and section-scoped loading and retry behavior. Authentication methods feed the summary from their existing single read. Audit warnings returned by existing user, session, authentication-method, and TAP writes remain visible. No activity, phishing, or sign-in data source was added.

The profile source-of-authority lock remains limited to profile editing in the UI. Independently capability-authorized security actions remain visible. No backend source-authority check, shared component, device-management flow, or role-management flow was changed.

## Changed files

- `.superpowers/sdd/plan/task-2-report.md`
- `src/Web/src/features/users/UserDetailPage.tsx`
- `src/Web/src/features/users/IdentitySection.tsx`
- `src/Web/src/features/users/JobInformationSection.tsx`
- `src/Web/src/features/users/GroupsSection.tsx`
- `src/Web/src/features/users/LicensesSection.tsx`
- `src/Web/src/features/users/RolesAndPimSection.tsx`
- `src/Web/src/features/users/AuthenticationMethodsSection.tsx`
- `src/Web/src/features/users/AssociatedDevicesSection.tsx`
- `src/Web/src/features/users/PasswordResetDialog.tsx`
- `src/Web/src/features/users/RevokeSessionsDialog.tsx`
- `src/Web/src/features/users/TemporaryAccessPassDialog.tsx`
- `src/Web/src/features/users/userDetailApi.ts`
- `src/Web/src/features/users/authenticationMethodsApi.ts`
- `src/Web/src/features/users/messages.ts`
- `src/Web/src/styles/theme.css`
- `tests/Web.UnitTests/features/users/UserDetailPage.test.tsx`
- `tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx`
- `tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx`

## TDD and verification

1. **Baseline**, before test changes:
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx ../../tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx`
   Result: **3 files passed, 34 tests passed**.
2. **RED**, after adding the tab, summary, authorization, retry, and single-read assertions but before implementing them:
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx ../../tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx --reporter=dot`
   Result: **8 expected failures, 34 passed**. Failures were missing tabs, summary, DomainAccessChip, source-independent action, card editor, method callback/retry, and device retry.
3. **Additional RED** for preserving audit warnings:
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx --reporter=dot 2>&1 | grep -E '^ FAIL|Test Files|Tests |AssertionError|TestingLibraryElementError|Expected|Received|ReferenceError|Error:' | head -80`
   Result: **3 expected failures** for missing TAP, password-reset, and session-revocation warning propagation.
4. **GREEN**, required focused command:
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx ../../tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx`
   Result: **3 files passed, 48 tests passed**.
5. Full behavior suite:
   `cd src/Web && npm run test:behavior`
   Result: **71 files passed, 482 tests passed**.
6. Type-check and production build:
   `cd src/Web && npm run build`
   Result: **passed**. Vite reported the existing ineffective `AuthProvider` dynamic import and large-chunk warnings.
7. `git diff --check`: passed.

## Concerns and follow-up

- The API's blanket source-of-authority check is intentionally unchanged for Task 4. As instructed, an independently authorized security action may appear in this UI while that server check still rejects it until Task 4 narrows the check.
- No live browser/E2E session was run. Focus movement, semantic relationships, module/capability gates, retries, unavailable copy, and stale-response protection are covered by the targeted behavior tests.
- The production build retains Vite's pre-existing dynamic-import and chunk-size warnings; this task did not alter bundling.

## Round 1 follow-up fixes

### Scope

Fixed only the two requested Important findings:

- A successful authentication-method removal/reset now clears the old list and reports the summary as loading while refreshing. A failed follow-up read clears the list, reports the summary as unavailable, and exposes the existing local read retry. Retrying does not repeat the successful mutation.
- Non-hidden `users.disable` and `authentication.methods.manage` decisions now render disabled controls wrapped with `DisabledReason` and decision-specific explanations. Hidden capability decisions remain suppressed. Disabled controls cannot dispatch mutations.

Preserved source-of-authority, module, role, and capability gates. No W0 or unrelated files changed.

### TDD and verification

1. **RED — removal refresh failure:**
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx --reporter=dot`
   Result: **1 expected failure, 7 passed**. The retry control was absent; the removed method remained visible and the failure surfaced as a mutation error.
2. **RED — capability matrices:**
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx --reporter=dot`
   Result: **16 expected failures, 43 passed**. Non-allowed disable and authentication-management actions were missing.
3. **RED — reset refresh failure:**
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx -t 'successful reset when refresh fails' --reporter=dot`
   Result: **1 expected failure, 17 skipped**. The successful reset left stale methods visible without a local read retry.
4. **GREEN — reset refresh failure:**
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx -t 'successful reset when refresh fails' --reporter=dot`
   Result: **1 passed, 17 skipped**. Retry refreshed the summary/list without repeating the reset.
5. **Focused GREEN:**
   `cd src/Web && npm run test:behavior -- ../../tests/Web.UnitTests/features/users/UserDetailPage.test.tsx ../../tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx ../../tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx`
   Result: **3 files passed, 68 tests passed**.
6. **Full behavior suite:**
   `cd src/Web && npm run test:behavior`
   Result: **71 files passed, 502 tests passed**.
7. **Type-check and production build:**
   `cd src/Web && npm run build`
   Result: **passed**. Vite emitted the existing ineffective `AuthProvider` dynamic-import and large-chunk warnings.
8. **Whitespace validation:**
   `git diff --check`
   Result: **passed**.

Follow-up code/test files: `UserDetailPage.tsx`, `AuthenticationMethodsSection.tsx`, and their two focused test files. No browser/E2E session was run.
