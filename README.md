# Atea Unified Workplace

## Requirements

- .NET SDK 9.x
- Node.js 22.x and npm 10+
- Docker Engine with Compose v2

## Run locally

Copy `.env.example` to `.env` for local configuration. `.env` is local-only and must not be committed. For the Development-only Atea platform console, set all six local-admin keys (use a stable GUID for the object ID):

```bash
AteaAdmin__LocalDevelopment__Enabled=true
AteaAdmin__LocalDevelopment__Username=local-admin
AteaAdmin__LocalDevelopment__Password=change-me-locally
AteaAdmin__LocalDevelopment__ObjectId=00000000-0000-0000-0000-000000000001
AteaAdmin__LocalDevelopment__DisplayName=Local Atea Administrator
AteaAdmin__LocalDevelopment__AllowAllWorkspaces=true
```

This local provider is accepted only when the API environment is `Development`. It is a development convenience, not an Atea production login, and `AllowAllWorkspaces=true` must never be carried into a deployed environment.

The local onboarding configuration must also be present in `.env`:

```text
Onboarding__PublicBaseUrl=http://localhost:5173
Onboarding__ConsentRedirectUri=http://localhost:5173/onboarding/consent/callback
Onboarding__ConsentSigningKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
```

Use exactly three terminals. Terminal 1 starts PostgreSQL; terminal 2 starts the API after PostgreSQL is healthy; terminal 3 starts Vite:

```bash
docker compose up -d postgres
docker compose ps postgres
dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj --urls http://localhost:8080
cd src/Web && npm ci && npm run dev -- --host 0.0.0.0 --port 5173
```

For the one-command Compose variant, `docker compose up --build` waits for the PostgreSQL health check before starting the API. The local API must receive `Onboarding__PublicBaseUrl=http://localhost:5173`; generated invitations use that origin and have no fallback origin. Tear down with `docker compose down` (add `-v` only when deliberately removing the local database volume).

Open `http://localhost:5173/admin` for the local Atea platform-admin login. Create the workspace, add the customer administrator membership, and create the one-time invitation. The customer administrator signs in through the customer tenant's Entra ID flow, redeems the invitation, grants admin consent for approved delegated Graph scopes, verifies the overview connection state, checks Global Reader read-only and User Administrator mutation states, and follows the PIM handoff for eligible, approval-required, and MFA-required states. The local Atea admin cookie is not a customer Entra session and cannot authorize customer Graph operations. The API health endpoint is `GET http://localhost:8080/health`.

Run `docker compose up --build` for the reproducible API, web and PostgreSQL stack.

## Azure infrastructure

The Azure deployment foundation is in [`infra/main.bicep`](infra/main.bicep), with environment examples in [`infra/parameters`](infra/parameters) and the operational runbook in [`docs/operations/azure-deployment.md`](docs/operations/azure-deployment.md). It uses the same combined Docker image as local development, private PostgreSQL networking, Key Vault references through managed identity, controlled HTTPS ingress, health probes, and multiple bounded revisions.

Run the credential-free infrastructure contract validation locally:

```bash
python3 infra/tests/validate_contract.py
```

This checks the Bicep parameter contract, environment-separated Entra values, secret-free parameter examples, exact hosted redirect URI policy, SPA build arguments, health/revision/ingress settings, and CI/runbook coverage. It does not require Azure credentials and does not deploy resources. Do not commit `.env` files, passwords, connection strings, signing keys, or tenant credentials.

## Tests

Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` and `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` for API tests. From the repository root, run `cd src/Web && npm ci && npm run build`, then run `npm run test --prefix tests/Web.UnitTests` and `npm run test --prefix tests/Web.E2E`. Run `docker compose config` to validate Compose.
