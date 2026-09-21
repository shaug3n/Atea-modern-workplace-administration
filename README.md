# Atea Unified Workplace

## Requirements

- .NET SDK 9.x
- Node.js 22.x and npm 10+
- Docker Engine with Compose v2

## Run locally

Copy `.env.example` to `.env` for local configuration. `.env` is local-only and must not be committed.

Run `dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj` and, from the repository root, `cd src/Web && npm ci && npm run dev` in separate terminals. The API health endpoint is `GET http://localhost:8080/health`.

Run `docker compose up --build` for the reproducible API, web and PostgreSQL stack.

## Azure infrastructure

The Azure deployment foundation is in [`infra/main.bicep`](infra/main.bicep), with environment examples in [`infra/parameters`](infra/parameters) and the operational runbook in [`docs/operations/azure-deployment.md`](docs/operations/azure-deployment.md). It uses the same combined Docker image as local development, private PostgreSQL networking, Key Vault references through managed identity, controlled HTTPS ingress, health probes, and multiple bounded revisions.

Run the credential-free infrastructure contract validation locally:

```bash
python3 infra/tests/validate_contract.py
```

This checks the Bicep parameter contract, environment-separated Entra values, secret-free parameter examples, exact redirect URI policy, health/revision/ingress settings, and CI/runbook coverage. It does not require Azure credentials and does not deploy resources. Do not commit `.env` files, passwords, connection strings, signing keys, or tenant credentials.

## Tests

Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` and `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` for API tests. From the repository root, run `cd src/Web && npm ci && npm run build`, then run `npm run test --prefix tests/Web.UnitTests` and `npm run test --prefix tests/Web.E2E`. Run `docker compose config` to validate Compose.
