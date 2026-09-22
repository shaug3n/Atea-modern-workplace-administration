# User and Device Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a spacious Atea-branded user and device administration experience with delegated, RBAC-aware sign-in security, associated Intune devices, and privileged device actions.

**Architecture:** The API remains the only Microsoft Graph caller and evaluates the signed-in user's Entra roles and delegated consent before every read or mutation. New Graph adapters add user-associated device reads, TAP issuance, session revocation, and privileged Intune operations; React consumes their typed, capability-aware contracts through a shared semantic action system and restructured user/device detail views.

**Tech Stack:** .NET 9 minimal APIs, EF Core/PostgreSQL idempotency and audit infrastructure, Microsoft Graph v1.0 delegated OAuth, React 18, TypeScript, Vitest/Testing Library, Vite, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-09-22-user-device-management-design.md`

## Global Constraints

- Use Microsoft Graph **v1.0** only; do not introduce Graph beta APIs or application permissions.
- The signed-in Entra user and their tenant RBAC remain the authorization source. The API, not the SPA, enforces every capability.
- TAP issuance is single-use, immediately available, valid for exactly 60 minutes, shown once, copy-enabled, and never persisted or logged.
- Every mutation validates route/action input, carries an idempotency key, writes a safe audit event, and maps Graph failures without raw Graph bodies, tokens, passwords, or secrets.
- Do not execute an unbounded tenant-wide device scan from a user-detail request. Association reads either use the targeted query or return a safe unavailable state.
- `DeviceManagementManagedDevices.PrivilegedOperations.All` is required for every device command, including existing sync and remote lock.
- Preserve Atea branding, Inter typography, dark-mode support, and the existing 8px token scale. Use explicit button classes; do not use positional `:first-child`/`:last-child` styling to indicate priority.
- Retire and wipe require typed confirmations and are never exercised against a non-disposable real device.

## Review Focus

- A replayed TAP request must never return, persist, audit, or log the original TAP code; the user receives a clear no-secret replay result.
- A Global Reader, a user missing delegated consent, and an inactive PIM user must not reach a mutation endpoint even if a stale SPA renders an action.
- A tenant that rejects `userId eq` must show an association-unavailable state, not a partial device list that looks authoritative and not a full-inventory scan.
- Device commands must require `devices.privileged.manage`; a `DeviceManagementManagedDevices.ReadWrite.All` grant alone must not enable Sync or Remote lock.
- Dark mode, narrow layouts, Escape/cancel handling, busy states, focus restoration and typed destructive confirmations must remain usable after the UI refactor.

## File Structure

| Path | Responsibility |
| --- | --- |
| `src/Api/Authorization/Capability.cs` | Canonical capability names, including session revocation and privileged device management. |
| `src/Api/Authorization/CapabilityEvaluator.cs` | Graph-scope and Entra-role requirements for the new capabilities. |
| `src/Api/Infrastructure/Graph/GraphScopeCatalog.cs` | Delegated Graph scope sets and capability-evaluation probes. |
| `src/Api/Features/Devices/UserAssociatedDeviceService.cs` | User-scoped managed-device API boundary without an unbounded fallback. |
| `src/Api/Infrastructure/Graph/GraphManagedDeviceReader.cs` | Targeted Intune association query and safe managed-device parsing. |
| `src/Api/Features/Identity/AuthenticationMethodService.cs` | TAP issuance command orchestration, redaction and method mutation rules. |
| `src/Api/Infrastructure/Graph/GraphAuthenticationMethodCommands.cs` | TAP creation and authentication-method Graph requests. |
| `src/Api/Features/Users/UserSessionCommandService.cs` | Session revocation orchestration, capability enforcement and auditing. |
| `src/Api/Infrastructure/Graph/GraphUserSessionCommands.cs` | Delegated `revokeSignInSessions` Graph adapter. |
| `src/Api/Features/Devices/DeviceCommandService.cs` | Privileged device-command authorization, idempotency and audit outcomes. |
| `src/Api/Infrastructure/Graph/GraphManagedDeviceCommands.cs` | Intune v1 action URI/body mapping. |
| `src/Web/src/components/ActionMenu.tsx` | Reusable accessible overflow command menu. |
| `src/Web/src/features/users/*` | User action bar, security summary/TAP dialog, device card and typed APIs. |
| `src/Web/src/features/devices/DevicesPage.tsx` | Device row/menu/detail-danger-zone interaction model. |
| `src/Web/src/styles/theme.css` | Semantic Atea layout, card, action and responsive style system. |
| `docs/security/entra-app-registration.md` | Exact delegated permission and re-consent instructions. |
| `docs/testing/local-user-device-management.md` | Local demonstration and safe test-tenant runbook. |

### Task 1: Establish the new delegated capability and consent foundation

**Files:**
- Modify: `src/Api/Authorization/Capability.cs`
- Modify: `src/Api/Authorization/CapabilityEvaluator.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphScopeCatalog.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphAuthorizationSnapshotReader.cs`
- Modify: `src/Web/src/capabilities/capabilityTypes.ts`
- Modify: `docs/security/entra-app-registration.md`
- Modify: `docs/security/entra-role-capability-matrix.md`
- Modify: `tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs`
- Modify: `tests/Api.UnitTests/Graph/GraphAuthorizationSnapshotReaderTests.cs`
- Test: `tests/Web.UnitTests/capabilities/PermissionState.test.tsx`

**Interfaces:**
- Produces `Capability.UsersRevokeSessions = "users.sessions.revoke"` and `Capability.DevicesPrivilegedManage = "devices.privileged.manage"` in both C# and TypeScript unions.
- Produces `GraphScopeCatalog.UserSessionWriteScopes = ["User.RevokeSessions.All"]` and `GraphScopeCatalog.DevicePrivilegedOperationScopes = ["DeviceManagementManagedDevices.PrivilegedOperations.All"]`.
- Later tasks consume these exact names; `Capability.DevicesManage` remains available for non-command device metadata operations but does not authorize an Intune action endpoint.

- [ ] **Step 1: Write the failing capability and scope-probe tests**

```csharp
[Fact]
public void Privileged_device_commands_require_privileged_operations_consent()
{
    var snapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["DeviceManagementManagedDevices.ReadWrite.All"],
        [ActiveRole(EntraRoleCatalog.IntuneAdministratorTemplateId)]);

    var decision = CapabilityEvaluator.Evaluate(snapshot, WorkspaceManager())[Capability.DevicesPrivilegedManage];

    decision.State.Should().Be(CapabilityState.ConsentRequired);
    decision.MissingScopes.Should().Contain("DeviceManagementManagedDevices.PrivilegedOperations.All");
}

[Fact]
public void Session_revocation_requires_user_revoke_sessions_scope()
{
    var snapshot = GraphAuthorizationSnapshot.Available(
        "actor-1", ["User.ReadWrite.All"], [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

    CapabilityEvaluator.Evaluate(snapshot, WorkspaceManager())[Capability.UsersRevokeSessions]
        .State.Should().Be(CapabilityState.ConsentRequired);
}
```

- [ ] **Step 2: Run the targeted tests and verify they fail because the capabilities do not exist**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~CapabilityEvaluatorTests"`

Expected: FAIL with missing capability constants or missing requirements.

- [ ] **Step 3: Implement the capability requirements and consent probes**

```csharp
public const string UsersRevokeSessions = "users.sessions.revoke";
public const string DevicesPrivilegedManage = "devices.privileged.manage";

[Capability.UsersRevokeSessions] = new(
    ReadScopes: ["Directory.Read.All", "User.Read.All"],
    WriteScopes: GraphScopeCatalog.UserSessionWriteScopes,
    RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId]),

[Capability.DevicesPrivilegedManage] = new(
    ReadScopes: GraphScopeCatalog.DeviceReadScopes,
    WriteScopes: GraphScopeCatalog.DevicePrivilegedOperationScopes,
    RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.IntuneAdministratorTemplateId, EntraRoleCatalog.CloudDeviceAdministratorTemplateId],
    MissingReadState: CapabilityState.ConsentRequired,
    ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
```

Add both Graph scope sets to `CapabilityEvaluationScopes` so missing-consent decisions identify their exact scopes. Extend the SPA `Capability` union verbatim. Update the registration document with delegated API-permission and tenant-admin-consent steps for `User.RevokeSessions.All` and `DeviceManagementManagedDevices.PrivilegedOperations.All`; document that existing ReadWrite device consent does not cover commands.

- [ ] **Step 4: Run the targeted API and SPA contract tests**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~CapabilityEvaluatorTests|FullyQualifiedName~GraphAuthorizationSnapshotReaderTests"`

Expected: PASS; the scope probe test asserts both new scopes are probed.

Run: `npm test -- capabilities/PermissionState.test.tsx`

Working directory: `tests/Web.UnitTests`

Expected: PASS; existing consent-required rendering remains intact for the expanded union.

- [ ] **Step 5: Commit the isolated foundation change**

```bash
git add src/Api/Authorization/Capability.cs src/Api/Authorization/CapabilityEvaluator.cs src/Api/Infrastructure/Graph/GraphScopeCatalog.cs src/Api/Infrastructure/Graph/GraphAuthorizationSnapshotReader.cs src/Web/src/capabilities/capabilityTypes.ts docs/security/entra-app-registration.md docs/security/entra-role-capability-matrix.md tests/Api.UnitTests/Authorization/CapabilityEvaluatorTests.cs tests/Api.UnitTests/Graph/GraphAuthorizationSnapshotReaderTests.cs tests/Web.UnitTests/capabilities/PermissionState.test.tsx
git commit -m "feat: model privileged administration capabilities"
```

### Task 2: Add a safe user-associated-device read boundary

**Files:**
- Modify: `src/Api/Features/Devices/DeviceContracts.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphManagedDeviceReader.cs`
- Create: `src/Api/Features/Devices/UserAssociatedDeviceService.cs`
- Create: `src/Api/Features/Devices/UserAssociatedDeviceEndpoints.cs`
- Modify: `src/Api/Program.cs`
- Modify: `tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs`
- Create: `tests/Api.UnitTests/Users/UserAssociatedDeviceServiceTests.cs`
- Modify: `tests/Api.IntegrationTests/Users/UserDetailEndpointTests.cs`

**Interfaces:**
- Consumes `Capability.DevicesView` from Task 1.
- Extends `IManagedDeviceReader` with `Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken)`.
- Produces `GET /api/users/{userObjectId}/devices` and `UserAssociatedDeviceResponse` containing `Items`, `FetchedAt`, `Freshness`, `PartialData`, `Access`, and optional safe `DeviceError`.

- [ ] **Step 1: Write failing Graph-reader and service tests**

```csharp
[Fact]
public async Task Associated_device_reader_issues_one_targeted_user_id_filter()
{
    var result = await reader.ReadForUserAsync("user-1", CancellationToken.None);

    result.Error.Should().BeNull();
    transport.Requests.Should().ContainSingle();
    transport.Requests.Single().PathAndQuery.Should()
        .Contain("%24filter=userId%20eq%20%27user-1%27");
}

[Fact]
public async Task Associated_device_service_does_not_fallback_to_an_inventory_scan_when_filter_is_rejected()
{
    var response = await service.GetAsync(Context(), "user-1", CancellationToken.None);

    response.Items.Should().BeEmpty();
    response.Error!.Category.Should().Be("association_query_unsupported");
    inventoryReader.ReadAsyncCalls.Should().Be(0);
}
```

- [ ] **Step 2: Run the new targeted tests and verify they fail**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests|FullyQualifiedName~UserAssociatedDeviceServiceTests"`

Expected: FAIL because `ReadForUserAsync`, `UserAssociatedDeviceService`, and the route contract do not exist.

- [ ] **Step 3: Implement the query, capability handling, endpoint and DI registration**

```csharp
public async Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(
    string userObjectId, CancellationToken cancellationToken)
{
    var query = new DeviceSearchQuery(PageSize: 100, UserObjectId: userObjectId);
    var page = await ReadAsync(query, cancellationToken);
    return page.Error is null
        ? GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Succeeded(page.Value.Items)
        : GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Failed(MapAssociationError(page.Error));
}
```

Add an optional `UserObjectId` to `DeviceSearchQuery`; when present, build exactly one `$filter=userId eq '{escaped-id}'` request and do not append continuation paging. Map Graph unsupported-filter failures to `association_query_unsupported`; do not leak the Graph message. `UserAssociatedDeviceService` evaluates `devices.view`, returns hidden/read-only/consent/PIM capability state consistently, and calls this reader only for allowed or read-only access. The endpoint validates the ID, resolves workspace context, and returns `403` for absent membership, `400` for unsafe IDs, or the typed response otherwise.

- [ ] **Step 4: Run API unit and integration coverage**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests|FullyQualifiedName~UserAssociatedDeviceServiceTests"`

Expected: PASS; one targeted query, no scan fallback, read-only response, consent-required response, and safe unsupported-filter result are covered.

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~UserDetailEndpointTests"`

Expected: PASS; workspace membership and unsafe-ID boundary behavior remain correct.

- [ ] **Step 5: Commit the user-device boundary**

```bash
git add src/Api/Features/Devices/DeviceContracts.cs src/Api/Infrastructure/Graph/GraphManagedDeviceReader.cs src/Api/Features/Devices/UserAssociatedDeviceService.cs src/Api/Features/Devices/UserAssociatedDeviceEndpoints.cs src/Api/Program.cs tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs tests/Api.UnitTests/Users/UserAssociatedDeviceServiceTests.cs tests/Api.IntegrationTests/Users/UserDetailEndpointTests.cs
git commit -m "feat: expose user-associated managed devices"
```

### Task 3: Implement TAP issuance and session revocation without secret persistence

**Files:**
- Modify: `src/Api/Features/Identity/AuthenticationMethodContracts.cs`
- Modify: `src/Api/Features/Identity/AuthenticationMethodEndpoints.cs`
- Modify: `src/Api/Features/Identity/AuthenticationMethodService.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphAuthenticationMethodCommands.cs`
- Create: `src/Api/Features/Users/UserSessionCommandService.cs`
- Create: `src/Api/Features/Users/UserSessionCommandEndpoints.cs`
- Create: `src/Api/Infrastructure/Graph/GraphUserSessionCommands.cs`
- Modify: `src/Api/Program.cs`
- Modify: `tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs`
- Create: `tests/Api.UnitTests/Users/UserSecurityCommandServiceTests.cs`
- Create: `tests/Api.IntegrationTests/Users/UserSecurityCommandEndpointTests.cs`

**Interfaces:**
- Consumes `Capability.AuthenticationMethodsManage` and `Capability.UsersRevokeSessions` from Task 1.
- Extends `IAuthenticationMethodCommands` with `Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken)`.
- Produces `POST /api/users/{id}/authentication-methods/temporary-access-pass` with a `TemporaryAccessPassCommandResult` that has an in-memory-only `TemporaryAccessPass` on first execution and no secret on replay.
- Produces `POST /api/users/{id}/revoke-sessions` using `IUserSessionCommandService`.

- [ ] **Step 1: Write failing secret-redaction, Graph-mapping and endpoint tests**

```csharp
[Fact]
public async Task First_tap_response_exposes_the_code_once_but_safe_idempotency_json_does_not_contain_it()
{
    var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

    result.TemporaryAccessPass.Should().Be("ABC123");
    idempotency.StoredSafeJson.Should().NotContain("ABC123");
    audit.SafeMetadataJson.Should().Be("{}");
}

[Fact]
public async Task Replayed_tap_request_never_returns_the_original_code()
{
    var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

    result.Replayed.Should().BeTrue();
    result.TemporaryAccessPass.Should().BeNull();
    result.Error.Should().Be("temporary_access_pass_already_issued");
}

[Fact]
public async Task Revoke_sessions_uses_the_v1_user_action_and_session_scope()
{
    await commands.RevokeAsync("user-1", "key-1", CancellationToken.None);

    transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/users/user-1/revokeSignInSessions");
    factory.Scopes.Single().Should().Equal(GraphScopeCatalog.UserSessionWriteScopes);
}
```

- [ ] **Step 2: Run the focused tests and confirm they fail**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~UserSecurityCommandServiceTests|FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`

Expected: FAIL because the TAP/session contracts and adapters do not exist.

- [ ] **Step 3: Implement Graph v1 adapters and safe command results**

```csharp
public async Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(
    string userObjectId, string idempotencyKey, CancellationToken cancellationToken)
{
    await using var lease = await clientFactory.CreateForCurrentUserAsync(
        GraphScopeCatalog.AuthenticationMethodWriteScopes, cancellationToken);
    using var body = new StringContent(
        JsonSerializer.Serialize(new { lifetimeInMinutes = 60, isUsableOnce = true }),
        Encoding.UTF8,
        "application/json");
    var response = await lease.Transport.SendAsync(new GraphRequest(
        HttpMethod.Post,
        $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/authentication/temporaryAccessPassMethods",
        body,
        new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }), cancellationToken);
    return GraphTemporaryAccessPassResult.From(response);
}

internal sealed record RevokeUserSessionsMutation(string UserObjectId)
    : JsonGraphMutation(GraphScopeCatalog.UserSessionWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery =>
        $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}/revokeSignInSessions";
    internal override object? Body => null;
}
```

`CreateTemporaryAccessPassAsync` must parse only `temporaryAccessPass`, `id`, `startDateTime`, `lifetimeInMinutes`, and `isUsableOnce`. It must retain the passcode only in the in-flight response object. Store a safe idempotency record without that property. On replay, return `temporary_access_pass_already_issued` and no code. The endpoint accepts no caller-controlled TAP body. Convert tenant policy/role/consent Graph failures to safe categories.

Add `UserSessionCommandService` following the existing `UserCommandService` idempotency/audit pattern with operation name `users.sessions.revoke`, target type `user`, and required capability `users.sessions.revoke`. Audit only IDs, outcomes and Graph correlation/request IDs.

- [ ] **Step 4: Run focused unit and integration tests**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~UserSecurityCommandServiceTests|FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`

Expected: PASS; TAP creates Graph v1 payload, code redaction and no-secret replay are asserted, and session revocation uses the correct scope/action.

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~UserSecurityCommandEndpointTests"`

Expected: PASS; bad ID, missing idempotency key, membership absence, denied capability and successful safe bodies are covered.

- [ ] **Step 5: Commit the user-security API work**

```bash
git add src/Api/Features/Identity/AuthenticationMethodContracts.cs src/Api/Features/Identity/AuthenticationMethodEndpoints.cs src/Api/Features/Identity/AuthenticationMethodService.cs src/Api/Infrastructure/Graph/GraphAuthenticationMethodCommands.cs src/Api/Features/Users/UserSessionCommandService.cs src/Api/Features/Users/UserSessionCommandEndpoints.cs src/Api/Infrastructure/Graph/GraphUserSessionCommands.cs src/Api/Program.cs tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs tests/Api.UnitTests/Users/UserSecurityCommandServiceTests.cs tests/Api.IntegrationTests/Users/UserSecurityCommandEndpointTests.cs
git commit -m "feat: add secure user sign-in actions"
```

### Task 4: Gate and implement privileged Intune device commands

**Files:**
- Modify: `src/Api/Features/Devices/DeviceContracts.cs`
- Modify: `src/Api/Features/Devices/DeviceEndpoints.cs`
- Modify: `src/Api/Features/Devices/DeviceCommandService.cs`
- Modify: `src/Api/Infrastructure/Graph/GraphManagedDeviceCommands.cs`
- Modify: `tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs`
- Create: `tests/Api.UnitTests/Devices/DeviceCommandServiceTests.cs`
- Create: `tests/Api.IntegrationTests/Devices/DeviceCommandEndpointTests.cs`

**Interfaces:**
- Consumes `Capability.DevicesPrivilegedManage` and `GraphScopeCatalog.DevicePrivilegedOperationScopes` from Task 1.
- Extends `DeviceActionNames` with `Restart = "restart"`, `Retire = "retire"`, and `Wipe = "wipe"`; preserves `Sync` and `RemoteLock`.
- Keeps `POST /api/devices/{deviceObjectId}/actions/{action}`, but the endpoint requires `devices.privileged.manage` and all result contracts identify that exact capability.

- [ ] **Step 1: Write failing action mapping and authorization tests**

```csharp
[Theory]
[InlineData(DeviceActionNames.Sync, "syncDevice")]
[InlineData(DeviceActionNames.RemoteLock, "remoteLock")]
[InlineData(DeviceActionNames.Restart, "rebootNow")]
[InlineData(DeviceActionNames.Retire, "retire")]
[InlineData(DeviceActionNames.Wipe, "wipe")]
public async Task Privileged_actions_use_v1_intune_mapping(string action, string graphAction)
{
    var transport = new RecordingTransport("{}");
    var factory = new RecordingFactory(transport);

    var result = await new GraphManagedDeviceCommands(factory)
        .ExecuteAsync("device-1", action, "key-1", CancellationToken.None);

    result.IsSuccess.Should().BeTrue();
    factory.Scopes.Single().Should().Equal(GraphScopeCatalog.DevicePrivilegedOperationScopes);
    transport.Requests.Single().PathAndQuery.Should()
        .Be($"/v1.0/deviceManagement/managedDevices/device-1/{graphAction}");
}

[Fact]
public async Task Device_command_is_denied_when_only_read_write_scope_is_granted()
{
    var result = await service.ExecuteAsync(ContextWithReadWriteOnly(), "device-1", DeviceActionNames.Sync, "key-1", CancellationToken.None);

    result.Status.Should().Be(DeviceCommandStatus.Denied);
    result.RequiredCapability.Should().Be(Capability.DevicesPrivilegedManage);
}
```

- [ ] **Step 2: Run the targeted tests and verify they fail**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~DeviceCommandServiceTests|FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`

Expected: FAIL because the actions, privileged capability and Graph mapping are incomplete.

- [ ] **Step 3: Implement command normalization, v1 payloads and capability enforcement**

```csharp
private static string GraphActionFor(string action) => action switch
{
    DeviceActionNames.Sync => "syncDevice",
    DeviceActionNames.RemoteLock => "remoteLock",
    DeviceActionNames.Restart => "rebootNow",
    DeviceActionNames.Retire => "retire",
    DeviceActionNames.Wipe => "wipe",
    _ => throw new ArgumentOutOfRangeException(nameof(action))
};

private static object? GraphBodyFor(string action) =>
    action == DeviceActionNames.Wipe
        ? new { keepEnrollmentData = false, keepUserData = false, persistEsimDataPlan = false }
        : null;
```

Make `ManagedDeviceActionMutation` take the mapped action and body and inherit `GraphScopeCatalog.DevicePrivilegedOperationScopes`. Change `DeviceCommandService` and endpoint filters to use `Capability.DevicesPrivilegedManage` for every action, including legacy sync and remote lock. Preserve input validation, idempotency operation names (`devices.sync`, `devices.remote-lock`, `devices.restart`, `devices.retire`, `devices.wipe`), safe audit metadata and error mapping. Do not add an action that mutates device fields.

- [ ] **Step 4: Run unit and integration tests**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj --filter "FullyQualifiedName~DeviceCommandServiceTests|FullyQualifiedName~GraphDeviceAndAuthenticationReaderTests"`

Expected: PASS; every action maps to Graph v1 with the privileged scope, wipe body omits secrets, and ReadWrite-only access is denied.

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter "FullyQualifiedName~DeviceCommandEndpointTests"`

Expected: PASS; endpoint capability requirement, idempotency, invalid action and workspace boundary are covered.

- [ ] **Step 5: Commit the privileged device command work**

```bash
git add src/Api/Features/Devices/DeviceContracts.cs src/Api/Features/Devices/DeviceEndpoints.cs src/Api/Features/Devices/DeviceCommandService.cs src/Api/Infrastructure/Graph/GraphManagedDeviceCommands.cs tests/Api.UnitTests/Infrastructure/Graph/GraphDeviceAndAuthenticationReaderTests.cs tests/Api.UnitTests/Devices/DeviceCommandServiceTests.cs tests/Api.IntegrationTests/Devices/DeviceCommandEndpointTests.cs
git commit -m "feat: add privileged Intune device actions"
```

### Task 5: Build the shared Atea action and layout system, then restructure the user blade

**Files:**
- Create: `src/Web/src/components/ActionMenu.tsx`
- Create: `tests/Web.UnitTests/components/ActionMenu.test.tsx`
- Create: `src/Web/src/features/users/AssociatedDevicesSection.tsx`
- Create: `src/Web/src/features/users/TemporaryAccessPassDialog.tsx`
- Create: `src/Web/src/features/users/RevokeSessionsDialog.tsx`
- Modify: `src/Web/src/features/users/authenticationMethodsApi.ts`
- Modify: `src/Web/src/features/users/userDetailApi.ts`
- Modify: `src/Web/src/features/users/AuthenticationMethodsSection.tsx`
- Modify: `src/Web/src/features/users/UserDetailPage.tsx`
- Modify: `src/Web/src/components/ConfirmationDialog.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/styles/theme.css`
- Modify: `tests/Web.UnitTests/features/users/UserDetailPage.test.tsx`
- Modify: `tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx`
- Create: `tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx`

**Interfaces:**
- Consumes `/api/users/{id}/devices` from Task 2 and the TAP/session endpoints from Task 3.
- Consumes `devices.privileged.manage`, `users.sessions.revoke`, and `authentication.methods.manage` from Task 1.
- Produces `ActionMenu` with `label`, `items`, `onOpenChange` and menu items that are buttons, keyboard reachable and Escape-dismissible.
- Produces `fetchAssociatedDevices`, `grantTemporaryAccessPass`, and `revokeUserSessions` typed web API helpers.

- [ ] **Step 1: Write failing user-flow and interaction tests**

```tsx
it('shows an associated managed device and opens its details link', async () => {
  render(<AssociatedDevicesSection userId="user-1" decision={allowedDevices} />);
  expect(await screen.findByText('WIN-TEST-01')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Open device WIN-TEST-01' }).getAttribute('href'))
    .toBe('/devices?device=device-1');
});

it('reveals a temporary access pass only on the first successful response', async () => {
  apiMock.mockResolvedValueOnce(ok({ status: 'succeeded', temporaryAccessPass: 'ABC123' }));
  renderSecuritySection();
  await issueTap();
  expect(await screen.findByText('ABC123')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Close' }));
  expect(screen.queryByText('ABC123')).toBeNull();
});

it('does not render user mutations for the Global Reader capability snapshot', async () => {
  render(<UserDetailPage userId="user-1" capabilities={[globalReaderDecision]} loadUserDetail={load} />);
  expect(screen.queryByRole('button', { name: 'Grant Temporary Access Pass' })).toBeNull();
});
```

- [ ] **Step 2: Run focused web tests and confirm they fail**

Run: `npm test -- features/users/UserDetailPage.test.tsx features/users/AuthenticationMethodsSection.test.tsx features/users/AssociatedDevicesSection.test.tsx components/ActionMenu.test.tsx`

Working directory: `tests/Web.UnitTests`

Expected: FAIL because the action menu, associated-device section and secure action dialogs do not exist.

- [ ] **Step 3: Implement semantic action components and page hierarchy**

```tsx
<div className="page-action-bar" aria-label="User management actions">
  {canEdit && <button className="button button--secondary" type="button">Edit user</button>}
  {canResetPassword && <button className="button button--primary" type="button">Reset password</button>}
  <ActionMenu label="Actions" items={actionItems} />
</div>

<div className="user-detail-grid">
  <ProfileSection user={user} access={detail.access} />
  <SecurityPostureSection user={user} roles={detail.roles} />
  <AuthenticationMethodsSection ... />
  <AssociatedDevicesSection userId={user.id} decision={devicesViewDecision} />
  <AccessSection licenses={detail.licenses} groups={detail.groups} roles={detail.roles} pim={detail.pim} />
</div>
```

`ActionMenu` must close on Escape, outside selection and action invocation, return focus to its trigger, and disable only individual busy entries. Update `ConfirmationDialog` to use explicit button classes rather than positional styling.

`TemporaryAccessPassDialog` posts without a body, displays the returned code in a transient dialog, supports `navigator.clipboard.writeText`, clears its local code state on close, and displays a replay or tenant-policy error without exposing a secret. `RevokeSessionsDialog` uses the standard confirmation component with a session-specific explanation. `AssociatedDevicesSection` renders `PermissionState` for consent/PIM states, fetches only when allowed/read-only, renders the safe unavailable message, and links a device to `/devices?device=<id>`.

Refactor `UserDetailPage` to the chosen profile/security/access layout while retaining all existing profile, group, license and PIM mutations. Add semantic CSS classes: `.page-action-bar`, `.button--primary`, `.button--secondary`, `.button--quiet`, `.button--danger`, `.action-menu`, `.user-detail-grid`, `.detail-card`, `.detail-card__header`, `.security-posture`, and `.associated-devices`. Use 32px page gaps, 24px card gaps/padding and 16px narrow viewport padding.

- [ ] **Step 4: Run focused UI tests and the full web unit suite**

Run: `npm test -- features/users/UserDetailPage.test.tsx features/users/AuthenticationMethodsSection.test.tsx features/users/AssociatedDevicesSection.test.tsx components/ActionMenu.test.tsx`

Working directory: `tests/Web.UnitTests`

Expected: PASS; reveal-once TAP, action permissions, associated devices, Escape/cancel, light/dark classes and action hierarchy are covered.

Run: `npm test`

Working directory: `tests/Web.UnitTests`

Expected: PASS; no regression across the existing SPA component suite.

- [ ] **Step 5: Commit the user UI overhaul**

```bash
git add src/Web/src/components/ActionMenu.tsx tests/Web.UnitTests/components/ActionMenu.test.tsx src/Web/src/features/users/AssociatedDevicesSection.tsx src/Web/src/features/users/TemporaryAccessPassDialog.tsx src/Web/src/features/users/RevokeSessionsDialog.tsx src/Web/src/features/users/authenticationMethodsApi.ts src/Web/src/features/users/userDetailApi.ts src/Web/src/features/users/AuthenticationMethodsSection.tsx src/Web/src/features/users/UserDetailPage.tsx src/Web/src/components/ConfirmationDialog.tsx src/Web/src/messages/en.ts src/Web/src/styles/theme.css tests/Web.UnitTests/features/users/UserDetailPage.test.tsx tests/Web.UnitTests/features/users/AuthenticationMethodsSection.test.tsx tests/Web.UnitTests/features/users/AssociatedDevicesSection.test.tsx
git commit -m "feat: overhaul user security management experience"
```

### Task 6: Refactor device management into explicit, privilege-aware operations

**Files:**
- Modify: `src/Web/src/features/devices/devicesApi.ts`
- Modify: `src/Web/src/features/devices/DevicesPage.tsx`
- Modify: `src/Web/src/messages/en.ts`
- Modify: `src/Web/src/styles/theme.css`
- Modify: `tests/Web.UnitTests/features/devices/DevicesPage.test.tsx`

**Interfaces:**
- Consumes `ActionMenu` from Task 5 and `devices.privileged.manage` from Task 1.
- Consumes extended device action union `sync | remote-lock | restart | retire | wipe` from Task 4.
- Produces a table with one Open details control and one action menu, plus a detail panel with Overview, Security and Danger zone groups.

- [ ] **Step 1: Write failing device action and destructive-confirmation tests**

```tsx
it('places privileged device actions in one accessible action menu', async () => {
  render(<DevicesPage capabilities={[devicesViewAllowed, devicesPrivilegedAllowed]} loadDevices={loadDevices} />);
  fireEvent.click(await screen.findByRole('button', { name: 'Actions for WIN-TEST-01' }));
  expect(screen.getByRole('menuitem', { name: 'Sync device' })).toBeTruthy();
  expect(screen.getByRole('menuitem', { name: 'Restart device' })).toBeTruthy();
  expect(screen.getByRole('menuitem', { name: 'Retire device' })).toBeTruthy();
});

it('requires WIPE and reviewed confirmation before issuing a wipe request', async () => {
  await openDeviceAction('Wipe device');
  fireEvent.change(screen.getByLabelText(/type the confirmation phrase/i), { target: { value: 'WIPE' } });
  fireEvent.click(screen.getByLabelText(/I reviewed the target/i));
  fireEvent.click(screen.getByRole('button', { name: 'Confirm action' }));
  await waitFor(() => expect(apiMock).toHaveBeenCalledWith('/api/devices/device-1/actions/wipe', expect.objectContaining({ method: 'POST' })));
});
```

- [ ] **Step 2: Run the focused device UI tests and verify they fail**

Run: `npm test -- features/devices/DevicesPage.test.tsx`

Working directory: `tests/Web.UnitTests`

Expected: FAIL because the actions and `devices.privileged.manage` gating are not implemented.

- [ ] **Step 3: Implement typed action state, menus, detail sections and spacing**

```tsx
type DeviceAction = 'sync' | 'remote-lock' | 'restart' | 'retire' | 'wipe';

const actionItems = privilegedDecision.state === 'allowed'
  ? [
      { label: 'Sync device', onSelect: () => requestAction(device, 'sync') },
      { label: 'Remote lock', onSelect: () => requestAction(device, 'remote-lock') },
      { label: 'Restart device', onSelect: () => requestAction(device, 'restart') },
      { label: 'Retire device', tone: 'danger', onSelect: () => requestAction(device, 'retire') },
      { label: 'Wipe device', tone: 'danger', onSelect: () => requestAction(device, 'wipe') },
    ]
  : [];
```

Change `executeDeviceAction` to accept the five-action union. Show the device page/list as read-only with a `PermissionState` explanation when the view is available but privileged operations are unavailable; do not leave failing Sync/Remote lock buttons visible. Keep Open details available to readers. Move restart/remote lock to the detail panel's Security section and retire/wipe into its Danger zone. Pass `REMOTE LOCK`, `RESTART`, `RETIRE`, or `WIPE` as a typed phrase where applicable; Sync may use reviewed confirmation only. Provide request-submitted success copy rather than claiming endpoint completion. Add page-specific CSS without weakening the shared semantic classes from Task 5.

- [ ] **Step 4: Run device tests, full SPA tests and build**

Run: `npm test -- features/devices/DevicesPage.test.tsx`

Working directory: `tests/Web.UnitTests`

Expected: PASS; reader view, missing consent, menu actions, typed dangerous confirmations, success/error state and Escape cancellation are covered.

Run: `npm test`

Working directory: `tests/Web.UnitTests`

Expected: PASS.

Run: `npm run build`

Working directory: `src/Web`

Expected: PASS; TypeScript and Vite compilation succeeds.

- [ ] **Step 5: Commit the device UI refactor**

```bash
git add src/Web/src/features/devices/devicesApi.ts src/Web/src/features/devices/DevicesPage.tsx src/Web/src/messages/en.ts src/Web/src/styles/theme.css tests/Web.UnitTests/features/devices/DevicesPage.test.tsx
git commit -m "feat: add privilege-aware device operations UI"
```

### Task 7: Verify the integrated local experience and publish the test runbook

**Files:**
- Create: `docs/testing/local-user-device-management.md`
- Modify: `docs/security/entra-app-registration.md`
- Modify: `tests/Web.E2E/local-v1-journey.spec.tsx`
- Modify: `tests/Web.E2E/local-compose-contract.test.mjs`
- Modify: `tests/Web.UnitTests/theme-and-catalog.test.mjs`

**Interfaces:**
- Consumes all API and SPA contracts from Tasks 1–6.
- Produces a safe, reproducible local demonstration path and end-to-end assertions that do not perform retire/wipe.

- [ ] **Step 1: Write failing integration-contract tests for scope guidance and semantic UI classes**

```js
test('local user-device guidance identifies the two additional delegated permissions', async () => {
  const runbook = await readFile('docs/testing/local-user-device-management.md', 'utf8');
  assert.match(runbook, /User\.RevokeSessions\.All/);
  assert.match(runbook, /DeviceManagementManagedDevices\.PrivilegedOperations\.All/);
});

test('theme defines semantic action and spacious detail card classes', async () => {
  const css = await readFile('src/Web/src/styles/theme.css', 'utf8');
  assert.match(css, /\.button--primary/);
  assert.match(css, /\.page-action-bar/);
  assert.match(css, /\.detail-card/);
});
```

- [ ] **Step 2: Run the contract tests and verify they fail**

Run: `npm test -- theme-and-catalog.test.mjs`

Working directory: `tests/Web.UnitTests`

Expected: FAIL because the semantic action CSS classes have not been added.

Run: `node --test local-compose-contract.test.mjs`

Working directory: `tests/Web.E2E`

Expected: FAIL because the local user/device runbook and protected-route assertions have not been added.

- [ ] **Step 3: Add the local runbook and safe E2E coverage**

The runbook must include exact local startup commands, expected health endpoints, the two Entra API-permission additions and admin-consent step, a Global Reader read-only check, an authorized/PIM activation check, a disposable-user TAP test, method reset/removal, associated-device check, non-destructive Sync check, and an explicit prohibition on Retire/Wipe for an everyday device. It must state that TAP codes are intentionally unrecoverable after closing their dialog.

Add E2E/browser-style checks that authenticate only through the existing fixture boundary, assert the action hierarchy and device-link route, and never submit a retire or wipe request. Update the Docker contract only to verify new API routes are served behind authentication; it must not invoke mutation endpoints.

- [ ] **Step 4: Run the full verification matrix**

Run: `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj`

Expected: PASS.

Run: `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj`

Expected: PASS.

Run: `npm test`

Working directory: `tests/Web.UnitTests`

Expected: PASS.

Run: `npm test`

Working directory: `tests/Web.E2E`

Expected: PASS.

Run: `npm run build`

Working directory: `src/Web`

Expected: PASS.

Run: `docker compose config --quiet`

Expected: PASS.

- [ ] **Step 5: Commit the verification and runbook work**

```bash
git add docs/testing/local-user-device-management.md docs/security/entra-app-registration.md tests/Web.E2E/local-v1-journey.spec.tsx tests/Web.E2E/local-compose-contract.test.mjs tests/Web.UnitTests/theme-and-catalog.test.mjs
git commit -m "docs: add local user and device management runbook"
```

## Plan self-review

- **Spec coverage:** Tasks 1–4 implement all delegated capability, consent, RBAC, Graph and audit requirements. Tasks 5–6 implement the action hierarchy, whitespace, user security, associated devices and device management experience. Task 7 supplies the required runbook and full verification matrix.
- **Shared-interface scan:** Task 1 produces capability names/scopes used by Tasks 2–6. Task 2 produces the associated-device endpoint consumed by Task 5. Task 3 produces TAP/session endpoints consumed by Task 5. Task 4 produces device action values consumed by Task 6. Task 5 produces shared action styles/menu consumed by Task 6. Each dependency is ordered and named consistently.
- **Placeholder scan:** No task relies on unspecified endpoints, generic error handling, an app-only authorization bypass, Graph beta, a tenant-wide fallback scan, or an untested destructive live action.
- **Review-focus coverage:** Task 3 tests one-time TAP redaction/replay; Tasks 1 and 3 test RBAC/consent/PIM enforcement; Task 2 tests no-scan association failure; Task 4 tests privileged device consent; Tasks 5–7 test responsive, keyboard and confirmation behaviours.
