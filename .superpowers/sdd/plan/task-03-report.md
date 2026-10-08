# Task 03 implementation report

## Scope

Implemented only F3 Task 03. The detail reader now selects and maps the primary user's display name and UPN, plus nullable reported encryption. These three optional fields are trailing parameters with `null` defaults, preserving existing `ManagedDeviceSummary` constructor calls. The configuration read scope is catalogued and independently included in capability scope evaluation; `DeviceReadScopes` and `DevicesView` requirements remain unchanged. Updated only the Devices directory/detail inventory row.

## TDD evidence

- Baseline command: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`
  - Initially blocked because `global.json` requires SDK `10.0.401`, while only SDK `9.0.318` was installed.
- After installing SDK `10.0.401` from Microsoft's official artifact and verifying its published SHA-512:
  - RED: `$HOME/.dotnet/dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`
  - Failed as expected at compile time because `ManagedDeviceSummary.UserDisplayName`, `.UserPrincipalName`, `.IsEncrypted`, and `GraphScopeCatalog.DeviceConfigurationReadScopes` did not yet exist.
- GREEN: same focused command after implementation
  - Passed: 37, failed: 0, skipped: 0.
- Full API unit suite: `$HOME/.dotnet/dotnet test tests/Api.UnitTests/Api.UnitTests.csproj`
  - Passed: 366, failed: 0, skipped: 0.
- `git diff --check`: passed.

## Self-review and concerns

- New fields are nullable and constructor-compatible; missing `isEncrypted` stays `null`.
- The detail projection is separate from the directory projection, so directory reads are not widened.
- Core detail reads still request only `DeviceManagementManagedDevices.Read.All`. The capability test confirms `DevicesView` remains allowed with that scope and without the optional configuration scope.
- No functional concerns identified. Tests use mocked Graph transport; they do not establish tenant consent, Intune role authorization, or live Graph behavior.
- No other F3 tasks, features, W0 components, or web dependencies were changed.
