# Atea Unified Workplace

## Requirements

- .NET SDK 9.x
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

The Azure deployment foundation is in [`infra/main.bicep`](infra/main.bicep), with environment examples in [`infra/parameters`](infra/parameters) and the operational runbook in [`docs/operations/azure-deployment.md`](docs/operations/azure-deployment.md). It uses the same combined Docker image as local development, private PostgreSQL networking, Key Vault references through managed identity, controlled HTTPS ingress, health probes, and multiple bounded revisions.

Run the credential-free infrastructure contract validation locally:

```bash
python3 infra/tests/validate_contract.py
```

This checks the Bicep parameter contract, environment-separated Entra values, secret-free parameter examples, exact hosted redirect URI policy, SPA build arguments, health/revision/ingress settings, and CI/runbook coverage. It does not require Azure credentials and does not deploy resources. Do not commit `.env` files, passwords, connection strings, signing keys, or tenant credentials.

## Tests

Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` and `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` for API tests. From the repository root, run `cd src/Web && npm ci && npm run build`, then run `npm run test --prefix tests/Web.UnitTests` and `npm run test --prefix tests/Web.E2E`. Run `docker compose config` to validate Compose.
