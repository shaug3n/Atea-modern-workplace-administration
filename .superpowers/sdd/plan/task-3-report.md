# F4 Task 3 delivery report

## Scope and boundaries

Implemented the authorized read-only hygiene snapshot service and `GET /api/licenses/hygiene`. Reused the committed `ILicenseOverviewReader` and `ILicenseHygieneUserReader`; did not modify the user reader, the Task 1 authorization gate, the F2 assignment flow, the F7 capability DTO/helpers, or W0 components. No writes, audit actions, persistence, caching, or new scopes were added.

The service evaluates `licenses.hygiene.view` before either data reader runs and permits only `allowed` or `read_only`. Inventory and user evidence are independently represented; verified user observations survive inventory failure and verified capacity survives user-scan failure. Graph categories, status codes, and retry delays are preserved with safe messages only.

## Finalized interfaces

- `LicenseHygieneSourceStatus(string Freshness, bool PartialData, DateTimeOffset? FetchedAt, LicenseHygieneError? Error)`
- `LicenseHygieneCoverage(int RecordsAssessed, bool Completed, string StopReason, int MissingEvidenceRecords)`
- `LicenseHygieneError(string Category, string Message, int? StatusCode = null, int? RetryAfterSeconds = null)`
- `LicenseHygieneAccess(string State, string? ReasonCode = null, CapabilityDecision? Authorization = null)`
- `LicenseHygieneSkuRow(string SkuId, string PartNumber, string DisplayName, int Purchased, int Assigned, int Available)`
- `LicenseHygieneAssignedSku(string SkuId, string PartNumber, string DisplayName)`
- `LicenseHygieneDisabledAccountRow(string Id, string? DisplayName, string? UserPrincipalName, IReadOnlyList<LicenseHygieneAssignedSku> AssignedLicenses, DateTimeOffset EvidenceAt, string EvidenceSource)`
- `LicenseHygieneResponse(LicenseHygieneAccess Access, LicenseHygieneSourceStatus Inventory, LicenseHygieneSourceStatus UserEvidence, LicenseHygieneCoverage Coverage, IReadOnlyList<LicenseHygieneSkuRow> CapacityItems, IReadOnlyList<LicenseHygieneDisabledAccountRow> DisabledAccounts)`
- `ILicenseHygieneService.RefreshAsync(WorkspaceContext, CancellationToken) -> Task<LicenseHygieneResponse>`

The endpoint requires authentication and the `license-hygiene` workspace module. Unknown valid SKU IDs are preserved as their identifiers; only explicit disabled status and valid nonempty assigned SKU IDs produce a finding. Missing/malformed required evidence is counted once per observed user.

## TDD and verification

Before implementation:

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~LicenseHygieneServiceTests` failed at compile time because `LicenseHygieneService` did not yet exist.
- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~LicenseHygieneEndpointTests` failed because the endpoint had not been mapped (the fallback returned 401 for those requests).

After implementation:

- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter FullyQualifiedName~LicenseHygieneServiceTests` — passed, 11/11.
- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~LicenseHygieneEndpointTests` — passed, 6/6.
- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~LicenseEndpointTests` — passed, 10/10.
- `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` — passed, 386/386.
- `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` — passed, 202 passed, 1 skipped (the existing opt-in Real Entra validation test), 203 total.

## Limitations

No live tenant validation was performed; unit and integration tests do not prove delegated Graph access in a tenant. No source scouting or additional permission validation was performed.
