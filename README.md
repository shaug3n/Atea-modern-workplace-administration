# Atea Unified Workplace

## Requirements

- .NET SDK 9.x
- Node.js 22.x and npm 10+
- Docker Engine with Compose v2

## Run locally

Copy `.env.example` to `.env` for local configuration. `.env` is local-only and must not be committed.

Run `dotnet run --project src/Api/Atea.UnifiedWorkplace.Api.csproj` and `npm ci && npm run dev --prefix src/Web` in separate terminals. The API health endpoint is `GET http://localhost:8080/health`.

Run `docker compose up --build` for the reproducible API, web and PostgreSQL stack.

## Tests

Run `dotnet test tests/Api.UnitTests/Api.UnitTests.csproj` and `dotnet test tests/Api.IntegrationTests/Api.IntegrationTests.csproj` for API tests. From `src/Web`, run `npm ci`; then run `npm run test --prefix ../../tests/Web.UnitTests` and `npm run test --prefix ../../tests/Web.E2E`. Run `docker compose config` to validate Compose.
