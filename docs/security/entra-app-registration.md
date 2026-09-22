# Entra app registrations

Task 2 uses separate Entra app registrations for development and production. The API validates bearer tokens issued for the workforce application and never accepts a tenant identifier supplied by the browser.

## Development registration

- Register a multitenant workforce SPA/API pair in the development Entra tenant.
- Approved development redirect URI set for the SPA: `http://localhost:5173/auth/callback` and `https://localhost:5173/auth/callback`. Register only the URI used by the local HTTPS/HTTP development profile and set `VITE_ENTRA_REDIRECT_URI` to that exact value.
- The API registration must also have a **Web** redirect URI for the in-app tenant-admin consent handoff: `http://localhost:5173/onboarding/consent/callback`. This is configured under **API app registration → Authentication → Add a platform → Web**; it is separate from the SPA sign-in callback and must be registered on the API app because the admin-consent request uses the API client ID.
- Expose the API scope named `access_as_user`; set `VITE_ENTRA_API_SCOPE` to the resulting `api://<development-api-client-id>/access_as_user` value.
- Set `VITE_ENTRA_CLIENT_ID` to the SPA client ID and `VITE_ENTRA_AUTHORITY` to the tenant-independent `https://login.microsoftonline.com/organizations` authority.

### Local Graph permissions

The API registration, not the SPA registration, must also have these Microsoft Graph **delegated** permissions for the local end-to-end test:

- `User.Read` (the default profile permission)
- `User.Read.All`
- `Group.Read.All`
- `Directory.Read.All`
- `User.Create`
- `User.ReadWrite.All`
- `User.EnableDisableAccount.All`
- `User-PasswordProfile.ReadWrite.All`
- `User.RevokeSessions.All`
- `GroupMember.ReadWrite.All`
- `LicenseAssignment.ReadWrite.All`
- `RoleManagement.Read.Directory`
- `RoleManagement.ReadWrite.Directory`

The API also reads the tenant's effective role-management policy assignments
when rendering PIM activation requirements. `RoleManagement.Read.Directory`
is used for that read; the policy response is never treated as a permission
grant and does not replace the signed-in user's active role or PIM state.

For the optional Devices and authentication-method modules, also add these
delegated permissions to the API registration and grant admin consent:

- `DeviceManagementManagedDevices.Read.All`
- `DeviceManagementManagedDevices.ReadWrite.All`
- `DeviceManagementManagedDevices.PrivilegedOperations.All`
- `UserAuthenticationMethod.Read.All`
- `UserAuthenticationMethod.ReadWrite.All`

If these optional permissions are not granted, the platform intentionally
hides the corresponding Devices and MFA/passkey operational sections while
keeping the core directory, licensing, PIM, and user-management features
available. The Identity and security permission-coverage table still shows
the unavailable capabilities and the exact scopes missing from the active
delegated token.

Session revocation requires `User.RevokeSessions.All`; ordinary user read/write
consent does not authorize that operation. Privileged Intune device commands
require `DeviceManagementManagedDevices.PrivilegedOperations.All`; existing
`DeviceManagementManagedDevices.ReadWrite.All` consent does not cover commands.
After adding these delegated permissions, a tenant administrator must grant
admin consent again.

After adding them under **API permissions → Microsoft Graph → Delegated permissions**, a tenant administrator must select **Grant admin consent** for the development tenant. This consent authorizes the API to perform delegated-on-behalf-of calls; it does not grant a user any Entra role. The platform still evaluates the signed-in user's active or PIM-activated Entra roles and keeps unsupported actions read-only or hidden.

For the local user/device demonstration, add both `User.RevokeSessions.All`
and `DeviceManagementManagedDevices.PrivilegedOperations.All` before selecting
**Grant admin consent**. Restart the local API and sign in again after consent
so a fresh delegated OBO token is acquired; the configured API-permission list
alone does not prove that either scope is present in the active token. The
second scope is required for Sync, Remote lock, Restart, Retire, and Wipe;
`DeviceManagementManagedDevices.ReadWrite.All` does not authorize those
commands by itself. Follow
`docs/testing/local-user-device-management.md` for the read-only, PIM, TAP,
associated-device, and safe Sync checks. Never put TAP codes, tokens, client
secrets, or other secret values in this document.

The in-app consent handoff uses the tenant-specific Microsoft Entra
`/v2.0/adminconsent` endpoint with `scope=https://graph.microsoft.com/.default`.
That scope tells Entra to present the Graph permissions configured on this API
registration, including permissions added after the initial tenant consent.

Do not add these Graph permissions to the SPA. The SPA should request only the API's `access_as_user` scope, and the API client secret must remain server-side.

### Troubleshooting delegated consent

The API permission list is configuration; it is not proof that the delegated
permissions are effective in the target tenant. The local API uses
on-behalf-of (OBO) token acquisition, so Microsoft Entra evaluates consent for
the API service principal in the signed-in tenant at runtime.

If the API log contains `AADSTS65001` for the local API application, repeat
these checks in the test tenant:

1. In **App registrations → Atea Unified Workplace API - Local → API
   permissions**, confirm the Graph entries are **Delegated permissions** and
   select **Grant admin consent** after adding any new permissions.
2. In **Enterprise applications**, open the service principal for the same API
   application ID and confirm its permissions/consent are for the test tenant,
   not the Atea home tenant or another directory.
3. Sign out of the SPA, clear the localhost site session or use a private
   window, then sign in again. Restart the API so its in-memory MSAL token
   cache is empty.
4. Confirm the API log shows the requested Graph scopes in the delegated token.
   A static green permission entry in the registration is not sufficient if
   the service principal consent was granted before those scopes were added.

The app uses the same distinction in its UI: “Configured permissions” is an
Entra registration state, while “Missing from active delegated token” is the
runtime state observed by the API. If the former is complete but the latter is
not, sign out and sign in again after restarting the local API to force fresh
OBO token acquisition.

The SPA should still show only `User.Read` and the API's `access_as_user`
scope. Do not fix an API OBO consent problem by adding Microsoft Graph
permissions to the SPA.

## Production registration

- Create a separate multitenant SPA/API registration and do not reuse development client IDs or redirect URIs.
- Approved production redirect URI set: `https://workplace.atea.com/auth/callback`. Register this exact HTTPS URI only; do not add wildcard or localhost URIs to the production registration. If the platform assigns a different approved Atea host, substitute that host through the deployment value `VITE_ENTRA_REDIRECT_URI` and update the registration and this allowlist together.
- Expose the same `access_as_user` API scope on the production API registration and use its exact scope value in `VITE_ENTRA_API_SCOPE`.
- Supply production API `ClientId` and `Audience` through deployment environment configuration (`AzureAd__ClientId` and `AzureAd__Audience`); no real identifiers belong in source control.

## Consent handoff

The platform owner supplies the API permission and scope details to each customer tenant administrator. The administrator grants consent for the approved multitenant application and scope, then the platform owner records the tenant's verified workspace membership separately. Consent does not itself grant workspace access: the server-side membership reader must match the verified token `tid` and `oid` before `/api/session` or later tenant data endpoints are available.

After a customer member has redeemed an invitation, that authenticated workspace member may start the admin-consent handoff from the workspace. This is deliberately separate from workspace-settings administration: the consent URL still goes to Microsoft Entra, where only a tenant administrator can approve the requested Graph delegated permissions. Tenant data and mutations remain governed by the signed-in user's effective Entra roles and the API's delegated token.

Do not add client secrets to the SPA, request Graph tokens in the browser, persist tokens in browser storage, or implement arbitrary tenant switching.

## Opt-in real-Entra integration validation

The API integration suite keeps synthetic JWT tests for deterministic local coverage and includes one explicitly gated test for real Microsoft Entra metadata. The real test is skipped with a clear reason, without starting the host or making a network call, unless `ATEA_REAL_ENTRA_RUN=true` and all of these environment variables are set in the test process:

- `ATEA_REAL_ENTRA_RUN`: must be exactly `true` (case-insensitive) to opt into network-dependent validation.

- `ATEA_REAL_ENTRA_AUTHORITY`: the Entra authority instance, normally `https://login.microsoftonline.com/`.
- `ATEA_REAL_ENTRA_TENANT_ID`: the dedicated non-production test tenant ID.
- `ATEA_REAL_ENTRA_CLIENT_ID`: the API app registration client ID for that test tenant.
- `ATEA_REAL_ENTRA_AUDIENCE`: the exact API audience accepted by the registration, such as `api://<test-api-client-id>`.
- `ATEA_REAL_ENTRA_ACCESS_TOKEN`: a short-lived access token issued for the API scope `access_as_user` by the dedicated test tenant.

Run it explicitly with `ATEA_REAL_ENTRA_RUN=true` and the five values above:

```text
ATEA_REAL_ENTRA_RUN=true DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~RealEntraValidationTests --disable-build-servers
```

The test uses the production Microsoft.Identity.Web configuration and live OpenID Connect metadata. It calls `/api/ping` with the supplied token and then with an intentionally invalid bearer value, expecting the valid request not to be rejected as `401` and the invalid request to return structured `401`. The token is read into process memory only; it is never logged, persisted, or included in test output. Use a dedicated test tenant, test app registration, approved API scope, and a manually issued short-lived token. Never use production tokens or commit any tenant IDs, credentials, or token values.
