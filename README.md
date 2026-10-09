# Atea Unified Workplace

## Requirements

- .NET SDK 10.0.401 (pinned in `global.json`)
- Node.js 22.x and npm 10+
- Docker Engine with Compose v2

## Run locally

Copy `.env.example` to `.env` for local configuration. `.env` is local-only and must not be committed. The example already enables the Development-only Atea platform console with the local credentials below; change the password locally if desired:

```bash
AteaAdmin__LocalDevelopment__Enabled=true
AteaAdmin__LocalDevelopment__Username=local-admin
AteaAdmin__LocalDevelopment__Password=change-me-locally
AteaAdmin__LocalDevelopment__ObjectId=00000000-0000-0000-0000-000000000001
AteaAdmin__LocalDevelopment__DisplayName="Local Atea Administrator"
AteaAdmin__LocalDevelopment__AllowAllWorkspaces=true
```

This local provider is accepted only when the API environment is `Development`. It is a development convenience, not an Atea production login, and `AllowAllWorkspaces=true` must never be carried into a deployed environment.

The local onboarding configuration is also included in `.env.example` and must remain present in `.env`:

```text
Onboarding__CustomerClientId=<customer-SPA-client-id>
Onboarding__ApiApplicationIdUri=api://<API-client-id>
Onboarding__PublicBaseUrl=http://localhost:5173
Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
Onboarding__ConsentSigningKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
Onboarding__TrustedProxyAddresses=
PlatformAuthorization__RequiredScope=platform.admin
```

Compose maps `VITE_ENTRA_CLIENT_ID` to `Onboarding__CustomerClientId` and
`AzureAd__Audience` to `Onboarding__ApiApplicationIdUri`; use the same customer
SPA client ID and API audience in the web/API configuration. The signing key
shown here is Development-only. Configure an explicit trusted proxy address
only when the deployment has a known proxy that forwards client IPs.

Use exactly three terminals. Source `.env` in each terminal so the API and Compose receive the same local configuration. Terminal 1 starts PostgreSQL:

```bash
set -a; . ./.env; set +a
docker compose up -d postgres
docker compose ps postgres
```

Terminal 2 starts the Development API with the local onboarding and admin settings:

```bash
set -a; . ./.env; set +a
dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj --urls http://localhost:8080
```

Terminal 3 starts Vite:

```bash
set -a; . ./.env; set +a
cd src/Web && npm ci && npm run dev -- --host 0.0.0.0 --port 5173
```

For the one-command Compose variant, source `.env` first: `set -a; . ./.env; set +a; docker compose up --build`. Compose explicitly configures the API as Development, injects the local admin credentials, injects all required onboarding values, uses the container PostgreSQL connection string, and waits for the PostgreSQL health check before starting the API. Generated invitations use `Onboarding__PublicBaseUrl=http://localhost:5173` and have no fallback origin. Tear down with `docker compose down` (add `-v` only when deliberately removing the local database volume).

Open `http://localhost:5173/admin` for the local Atea platform-admin login. Platform bearer calls require the configured `platform.admin` scope in the space-delimited `scp` claim; the Development-only local admin cookie is exempt from bearer scopes. Create the workspace using the customer's verified tenant domain or tenant GUID—an email domain is never inferred—then create the nominated administrator's invitation. For eligible administrator invitations, the customer opens one link while signed out, reviews the delegated-permission request, grants consent in the customer tenant, and then signs in as the invited identity to redeem and complete automatic verification. Ordinary member invitations retain sign-in and redemption without anonymous consent start. The local Atea admin cookie is not a customer Entra session and cannot authorize customer Graph operations. The API health endpoint is `GET http://localhost:8080/health`.

## Consent-first customer onboarding

The Atea operator remains responsible for verifying the customer's tenant,
creating the workspace and handing off the invitation through the approved
channel. Domain input is resolved through Microsoft Entra OpenID Connect
metadata before provisioning; GUID input remains supported. The discovery
result identifies a directory but is not proof of domain ownership or
authorization.

For an eligible administrator invitation:

1. The invitee opens the invitation page before signing in. It shows the
   workspace name, setup steps and delegated permission details without
   exposing invitee identity, tenant/workspace IDs or connection diagnostics.
2. The invitee starts tenant-specific admin consent. Entra provisions the
   customer's SPA/API service principals and grants only the configured
   delegated permissions. The app does not use app-only Graph access, create
   enterprise apps itself, assign directory roles, or create membership during
   anonymous consent.
3. After returning to the same browser tab, the invitee signs in at the
   server-returned tenant authority as the nominated identity. The API verifies
   tenant and object ID, redeems the invitation, consumes the bound challenge,
   and checks delegated connection/permission coverage. `consent_received`
   means callback processing only; it does not mean the connection is healthy.
4. Full permission coverage reaches the overview. Missing permissions or
   inconclusive verification remains visible with safe retry/re-consent
   guidance. A retry performs a new health check rather than replaying the
   consumed consent state.

The customer SPA lists the API's `access_as_user` scope; the API registration
lists the reviewed Graph delegated-scope manifest and links the customer SPA
through `knownClientApplications`. For this consent-first route, the admin
consent request targets the customer SPA and requests the API resource's
`/.default` URI. This differs from the retained authenticated legacy
re-consent route, which targets the API and uses Graph `/.default`. Consent
does not grant an Entra role: the customer's active directory role, PIM state,
workspace membership and API authorization remain separate checks.

To inspect registration changes without writing to Entra, use the existing
registration tool's default dry-run mode. Supply the expected home tenant,
existing API/customer-SPA client IDs, API application-ID URI and exact callback
URIs:

```bash
python3 infra/scripts/configure-entra-onboarding.py \
  --expected-home-tenant-id "$ENTRA_HOME_TENANT_ID" \
  --api-app-id "$ENTRA_API_CLIENT_ID" \
  --customer-spa-app-id "$CUSTOMER_SPA_CLIENT_ID" \
  --api-application-id-uri "$ENTRA_API_AUDIENCE" \
  --sign-in-redirect-uri "$APP_PUBLIC_URL/auth/callback" \
  --consent-redirect-uri "$APP_PUBLIC_URL/onboarding/consent/callback"
```

Review the sanitized diff with the registration owner before considering
`--apply`. The script does not create registrations, grant admin consent,
create secrets, change customer tenants or assign roles. Do not run it with
`--apply` as part of local validation.

## Azure infrastructure

Azure deployment is split into the disposable foundation in [`infra/main.bicep`](infra/main.bicep), one-time private database bootstrap in [`infra/database-bootstrap.bicep`](infra/database-bootstrap.bicep), and application release in [`infra/application.bicep`](infra/application.bicep). GitHub Actions has a manually dispatched foundation workflow and an automatic test-release workflow (candidate deploy after `test` approval for `master` and same-repository PRs; promotion only from `master` after `test-promotion` approval); ACA ingress is restricted to the test operator's IP, and a stable revision-label URL supports interactive test-tenant validation before routing traffic. The runbook in [`docs/operations/azure-deployment.md`](docs/operations/azure-deployment.md) documents Entra callbacks, GitHub OIDC, secrets, migrations, rollback, cost alerts, and the later Atea handoff. No live Azure resources are created by local validation.

Run the credential-free infrastructure contract validation locally:

```bash
python3 infra/tests/validate_contract.py
```

This checks staged Bicep inputs, secret-free parameter examples, distinct callback paths, migration job settings, SPA build arguments, revision/traffic ordering, ingress allowlists, and runbook coverage. To compile all three templates, run `az bicep build --file infra/main.bicep`, `az bicep build --file infra/application.bicep`, and `az bicep build --file infra/database-bootstrap.bicep`. Neither validation step requires Azure credentials or deploys resources. Do not commit `.env` files, passwords, connection strings, signing keys, or tenant credentials.

## Tests

Use the pinned .NET SDK and run the API suites with:

```bash
dotnet test tests/Api.UnitTests/Api.UnitTests.csproj
dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj
```

Install web dependencies with `npm ci --prefix src/Web`, then run the
Vitest/jsdom component suite and production build:

```bash
npm run test:behavior --prefix src/Web -- --run
npm run build --prefix src/Web
```

Run the Node TAP contracts and E2E-directory browser-boundary suite with:

```bash
npm test --prefix tests/Web.UnitTests
npm test --prefix tests/Web.E2E
```

The `tests/Web.E2E` command runs TAP contracts plus Vitest/jsdom scenarios; it
does not launch a real browser or sign in to Entra. These deterministic fixtures
do not verify live Graph permissions, tenant consent, Entra roles, or PIM
policy. Follow the dedicated test-tenant runbooks for those real-tenant checks.
Run `python3 infra/tests/test_configure_entra_onboarding.py`,
`python3 infra/tests/validate_contract.py`, and `docker compose config --quiet`
for the registration-script, hosted-infrastructure and local Compose
contracts. The API integration suite uses Testcontainers PostgreSQL and needs a
running Docker daemon; do not replace its database tests with an in-memory
provider. Live tenant acceptance remains an explicit, opt-in operation and is
not implied by these deterministic checks.
