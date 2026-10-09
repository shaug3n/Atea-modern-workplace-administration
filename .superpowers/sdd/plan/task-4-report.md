# Task 4 report: API reason validation, audit, and idempotency

## Outcome

Completed Task 4 on the existing worktree changes. Added required reason normalization at the F2 HTTP write boundaries, carried the normalized reason through the existing command and audit paths, and included it in the existing idempotency fingerprints without adding it to safe result JSON or Graph request bodies. Preserved the profile-only source-authority lock and existing authorization, catalog, target-match, audit-warning, and capability paths.

No prior `task-4-report.md` existed when resuming. The interrupted attempt provided no recoverable test or red/green evidence, so none is claimed here.

## Implementation and coverage

- Added `UserWriteReasonValidation.TryNormalize`: trim, reject blank as `reason_required`, reject normalized length over 1,000 as `reason_too_long`.
- Added optional trailing `Reason` command fields and the body contract for previously bodyless actions; retained the password reset endpoint’s existing service operation and Graph payload behavior.
- Normalized/validated request reasons before service dispatch, including body-required reason validation for DELETE group/license and auth-method removal.
- Passed reasons through user, authentication-method, and session audits as `{ "reason": normalizedReason }`; existing Graph correlation/request IDs and audit persistence warnings remain.
- Included reason in existing idempotency request payloads for user commands, auth-method remove/reset/TAP, and session revoke. Safe replay JSON does not contain reason or temporary credentials/TAP values.
- Kept `GraphLicenseService` method signatures and Graph body unchanged; its command object carries the additive reason field, which the adapter ignores.
- Added focused unit/integration coverage for normalization, invalid HTTP reasons with zero dispatch/audit, trimmed audit metadata, audit failure warnings, changed-reason idempotency conflicts, source-lock boundaries, and secret-safe replay behavior.

## Verification

`git diff --check` — passed.

Requested unit command:

```text
dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~UserCommandServiceTests|FullyQualifiedName~UserSecurityCommandServiceTests"
```

Could not run: the repository pins SDK `10.0.401`; only SDK `9.0.318` is installed. Output:

```text
Requested SDK version: 10.0.401
Installed SDKs:
9.0.318 [/usr/local/share/dotnet/sdk]
```

Tried invoking the unit project from outside the repository to use SDK 9. Restore/build then stopped with `NETSDK1045`: SDK 9 does not support targeting `.NET 10.0`.

Requested integration command:

```text
dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~UserMutationEndpointTests|FullyQualifiedName~UserSecurityCommandEndpointTests"
```

Could not run for the same pinned-SDK mismatch. Therefore no claim is made that tests compiled or passed.

## Changed files

- `src/Api/Features/Users/UserWriteReasonValidation.cs` (new)
- `src/Api/Features/Users/UserCommandContracts.cs`
- `src/Api/Features/Users/UserCommandEndpoints.cs`
- `src/Api/Features/Users/UserCommandService.cs`
- `src/Api/Features/Users/UserSessionCommandEndpoints.cs`
- `src/Api/Features/Users/UserSessionCommandService.cs`
- `src/Api/Features/Identity/AuthenticationMethodContracts.cs`
- `src/Api/Features/Identity/AuthenticationMethodEndpoints.cs`
- `src/Api/Features/Identity/AuthenticationMethodService.cs`
- `tests/Api.UnitTests/Users/UserCommandServiceTests.cs`
- `tests/Api.UnitTests/Users/UserSecurityCommandServiceTests.cs`
- `tests/Api.IntegrationTests/Users/UserMutationEndpointTests.cs`
- `tests/Api.IntegrationTests/Users/UserSecurityCommandEndpointTests.cs`

## Concerns

- API unit and integration behavior remains unverified by execution until SDK `10.0.401` is available.
