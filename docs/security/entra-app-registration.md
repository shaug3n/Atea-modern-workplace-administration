# Entra app registrations

Development and hosted environments use separate Entra app registrations. The API validates bearer tokens issued for the workforce application and never accepts a tenant identifier supplied by the browser.

## Development registration

- Register a multitenant workforce customer SPA/API pair in the Atea home tenant and keep the separate platform-admin SPA out of customer consent.
- Approved development customer SPA **SPA** sign-in redirect is `http://localhost:5173/auth/callback` (or the exact HTTPS profile URI when that profile is used). Set `VITE_ENTRA_REDIRECT_URI` to that exact value.
- The customer SPA registration also has the consent-first `/onboarding/consent/callback` URI under its **Web** platform. This admin-consent response returns parameters rather than an authorization code. The API registration retains its own Web `/onboarding/consent/callback` URI for the legacy authenticated API-client/Graph consent flow.
- Expose the API scope named `access_as_user`; set `VITE_ENTRA_API_SCOPE` to the resulting `api://<development-api-client-id>/access_as_user` value.
- Set `VITE_ENTRA_CLIENT_ID` to the SPA client ID and `VITE_ENTRA_AUTHORITY` to the tenant-independent `https://login.microsoftonline.com/organizations` authority.
- Configure `Onboarding__CustomerClientId` with the customer SPA client ID and `Onboarding__ApiApplicationIdUri` with the API's actual application-ID URI (normally `api://<development-api-client-id>`). The consent-first request uses `<API application-ID URI>/.default`; it does not construct the resource URI from browser input.

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
- `BitlockerKey.ReadBasic.All` (BitLocker metadata)
- `BitlockerKey.Read.All` (BitLocker key reveal)
- `DeviceLocalCredential.ReadBasic.All` (Windows LAPS metadata)
- `DeviceLocalCredential.Read.All` (Windows LAPS password reveal)
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

Recovery metadata and secret permissions are separate. The Atea API app owner
adds them under Microsoft Graph delegated API permissions on the app
registration; a customer tenant administrator grants tenant consent afterward.
Consent cannot add an unregistered permission. The signed-in user's device
ownership, Entra role/PIM activation, and scope are still checked by Graph for
the specific recovery record. A workspace Devices assignment is not a Microsoft
recovery grant.

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

The retained authenticated member re-consent handoff targets the API client ID
and uses the tenant-specific Microsoft Entra `/v2.0/adminconsent` endpoint with
`scope=https://graph.microsoft.com/.default`. That scope presents the Graph
permissions configured on the API registration, including permissions added
after initial tenant consent. The consent-first administrator-invitation flow
is different: it targets the customer SPA client ID with the API resource's
configured `/.default` scope so Entra can bundle the SPA → API delegated grant
and the API → Graph delegated grants.

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
- Approved production customer SPA **SPA** sign-in URI: `https://workplace.atea.com/auth/callback`; customer SPA **Web** consent response URI: `https://workplace.atea.com/onboarding/consent/callback`; API **Web** legacy consent URI: `https://workplace.atea.com/onboarding/consent/callback`. Register exact HTTPS URIs only; do not add wildcard or localhost URIs to production. If the platform assigns another approved Atea host, update the app registration and deployment values together.
- Expose the same `access_as_user` API scope on the production API registration and use its exact scope value in `VITE_ENTRA_API_SCOPE`.
- Supply production API `ClientId` and `Audience` through deployment environment configuration (`AzureAd__ClientId` and `AzureAd__Audience`); no real identifiers belong in source control.

## Consent-first registration linkage and permissions

The customer SPA and API must be existing Atea-owned registrations in the same
home tenant. Both use `AzureADMultipleOrgs`. The API's
`knownClientApplications` includes the customer SPA app ID; the API publishes
`access_as_user`; the customer SPA requests that API scope. The API's Graph
delegated permission set is the reviewed, sorted
`infra/entra/delegated-permissions.json` manifest derived from the capability
scope catalog, including optional module scopes. The customer SPA must not
request Graph permissions directly or `platform.admin`; the separate
platform-admin SPA is never linked into this customer consent bundle.

Use the standard library registration script to inspect or converge these
settings on the **existing** registrations:

```bash
python3 infra/scripts/configure-entra-onboarding.py \
  --expected-home-tenant-id "$ENTRA_HOME_TENANT_ID" \
  --api-app-id "$ENTRA_API_CLIENT_ID" \
  --customer-spa-app-id "$CUSTOMER_SPA_CLIENT_ID" \
  --api-application-id-uri "$ENTRA_API_AUDIENCE" \
  --sign-in-redirect-uri "$APP_PUBLIC_URL/auth/callback" \
  --consent-redirect-uri "$APP_PUBLIC_URL/onboarding/consent/callback"
```

The default is a sanitized dry-run. The script checks the Azure CLI home
tenant, resolves enabled delegated scope IDs by name, preserves unrelated
resource permissions and redirect URIs, and refuses unexpected Graph
application permissions on the API or direct Graph/`platform.admin`
permissions on the customer SPA. Review the diff with the registration owner;
only a separately authorized operator should add `--apply`. The script
re-reads registrations after writes, but does not create apps, grant admin
consent, create service principals in customer tenants, add secrets, assign
roles, or modify customer directories. Its tests use fake CLI/Graph responses;
they are not live-tenant validation.

The customer admin consent screen must show both SPA → API and API → Graph
delegated grants in a dedicated fresh tenant. If registration linkage does not
produce the single expected screen, stop rollout and investigate the
registrations. Do not fall back silently to two consent screens or app-only
permissions. Consent grants application permissions only; it does not redeem
the invitation, create workspace membership, assign Entra roles, or bypass
active-role/PIM and tenant policy checks.

## Consent handoff

For an eligible `customer_admin` or `workspace_owner` invitation, the
administrator opens the invitation while signed out and reviews the displayed
delegated permission summary. The consent-first start endpoint derives tenant,
client, redirect and API resource scope from server-side invitation/configuration
data. The invitation's signed state and hash are bound to the tenant,
workspace, invitation and expiry; raw nonce/state values must not be logged.
Pending browser state is tab-scoped in `sessionStorage`.

After returning to the configured callback, the browser asks the API to resume
the invitation transaction and signs in at the tenant returned by the server.
Only the authenticated invited identity can redeem and complete the invitation.
The API then automatically verifies delegated access and reports connected,
missing or unknown scopes honestly. Callback `tenant`, `admin_consent`, raw
provider errors and consent itself are not identity or authorization evidence.
Ordinary member invitations retain sign-in/redemption without anonymous
consent-start access. After sign-in, an authenticated workspace member may
still use the legacy API-client/Graph-`/.default` re-consent and explicit
health-check route.

Consent does not itself grant workspace access: the server-side membership
reader must match the verified token `tid` and `oid` before `/api/session` or
later tenant data endpoints are available. The API verifies customer
membership, the invitation's nominated identity and current consent/Graph
state independently.

Do not add client secrets to the SPA or request Graph tokens in the browser.
MSAL keeps its normal API-authentication cache in the configured tab
`sessionStorage`; do not copy tokens into application-managed storage, logs or
other origins. The consent transaction also uses tab-scoped `sessionStorage`,
and arbitrary tenant switching is not supported.

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
