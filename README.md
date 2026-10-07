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
Onboarding__PublicBaseUrl=http://localhost:5173
Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
Onboarding__ConsentSigningKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
PlatformAuthorization__RequiredScope=platform.admin
```

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

Open `http://localhost:5173/admin` for the local Atea platform-admin login. Platform bearer calls require the configured `platform.admin` scope in the space-delimited `scp` claim; the Development-only local admin cookie is exempt from bearer scopes. Create the workspace, add the customer administrator membership, and create the one-time invitation. The customer administrator signs in through the customer tenant's Entra ID flow, redeems the invitation, grants admin consent for approved delegated Graph scopes, verifies the overview connection state, checks Global Reader read-only and User Administrator mutation states, and follows the PIM handoff for eligible, approval-required, and MFA-required states. The local Atea admin cookie is not a customer Entra session and cannot authorize customer Graph operations. The API health endpoint is `GET http://localhost:8080/health`.

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
Run `docker compose config --quiet` to validate Compose.
