# Entra app registrations

Task 2 uses separate Entra app registrations for development and production. The API validates bearer tokens issued for the workforce application and never accepts a tenant identifier supplied by the browser.

## Development registration

- Register a multitenant workforce SPA/API pair in the development Entra tenant.
- Approved development redirect URI set: `http://localhost:5173/auth/callback` and `https://localhost:5173/auth/callback`. Register only the URI used by the local HTTPS/HTTP development profile and set `VITE_ENTRA_REDIRECT_URI` to that exact value.
- Expose the API scope named `access_as_user`; set `VITE_ENTRA_API_SCOPE` to the resulting `api://<development-api-client-id>/access_as_user` value.
- Set `VITE_ENTRA_CLIENT_ID` to the SPA client ID and `VITE_ENTRA_AUTHORITY` to the tenant-independent `https://login.microsoftonline.com/organizations` authority.

## Production registration

- Create a separate multitenant SPA/API registration and do not reuse development client IDs or redirect URIs.
- Approved production redirect URI set: `https://workplace.atea.com/auth/callback`. Register this exact HTTPS URI only; do not add wildcard or localhost URIs to the production registration. If the platform assigns a different approved Atea host, substitute that host through the deployment value `VITE_ENTRA_REDIRECT_URI` and update the registration and this allowlist together.
- Expose the same `access_as_user` API scope on the production API registration and use its exact scope value in `VITE_ENTRA_API_SCOPE`.
- Supply production API `ClientId` and `Audience` through deployment environment configuration (`AzureAd__ClientId` and `AzureAd__Audience`); no real identifiers belong in source control.

## Consent handoff

The platform owner supplies the API permission and scope details to each customer tenant administrator. The administrator grants consent for the approved multitenant application and scope, then the platform owner records the tenant's verified workspace membership separately. Consent does not itself grant workspace access: the server-side membership reader must match the verified token `tid` and `oid` before `/api/session` or later tenant data endpoints are available.

Do not add client secrets to the SPA, request Graph tokens in the browser, persist tokens in browser storage, or implement arbitrary tenant switching.

## Opt-in real-Entra integration validation

The API integration suite keeps synthetic JWT tests for deterministic local coverage and includes one opt-in test for real Microsoft Entra metadata. The real test is skipped with a clear reason, without starting the host or making a network call, unless all of these environment variables are set in the test process:

- `ATEA_REAL_ENTRA_AUTHORITY`: the Entra authority instance, normally `https://login.microsoftonline.com/`.
- `ATEA_REAL_ENTRA_TENANT_ID`: the dedicated non-production test tenant ID.
- `ATEA_REAL_ENTRA_CLIENT_ID`: the API app registration client ID for that test tenant.
- `ATEA_REAL_ENTRA_AUDIENCE`: the exact API audience accepted by the registration, such as `api://<test-api-client-id>`.
- `ATEA_REAL_ENTRA_ACCESS_TOKEN`: a short-lived access token issued for the API scope `access_as_user` by the dedicated test tenant.

Run it explicitly with:

```text
DOTNET_CLI_HOME=/private/tmp/atea-dotnet-home NUGET_PACKAGES=/private/tmp/atea-nuget dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj --filter FullyQualifiedName~RealEntraValidationTests --disable-build-servers
```

The test uses the production Microsoft.Identity.Web configuration and live OpenID Connect metadata. It calls `/api/ping` with the supplied token and then with an intentionally invalid bearer value, expecting the valid request not to be rejected as `401` and the invalid request to return structured `401`. The token is read into process memory only; it is never logged, persisted, or included in test output. Use a dedicated test tenant, test app registration, approved API scope, and a manually issued short-lived token. Never use production tokens or commit any tenant IDs, credentials, or token values.
