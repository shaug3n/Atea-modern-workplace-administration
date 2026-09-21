# Azure deployment runbook

This runbook covers the v1 single-region Azure foundation for the combined ASP.NET Core API and React SPA image. The declarative entrypoint is [`infra/main.bicep`](../../infra/main.bicep); it creates ACR, private PostgreSQL Flexible Server networking, Key Vault, Log Analytics/Application Insights, a managed Container Apps environment, HTTPS ingress and a revision-bounded app.

## Provisioning prerequisites

- An Azure subscription and non-production resource group in an Atea-approved EU/EEA region. `westeurope` is the safe development default.
- Azure CLI with the Bicep extension, Docker, and permission to create the listed resources and role assignments.
- A federated GitHub Actions identity with `AcrPush`, `Contributor` on the deployment resource group, and permission to start the approved migration job.
- Two separate Entra app registrations: development and production. Register only exact redirect URIs; wildcard redirect URIs are forbidden. The production default is `https://workplace.atea.com/auth/callback`.
- DNS control for every hostname in `allowedIngressHostnames`. Azure Container Apps managed certificates use CNAME validation.

Before the first app deployment, create the resource group, put the PostgreSQL administrator password in the CI secret store, and deploy the Bicep template with that value supplied at invocation time. Never write the password to a parameter file or shell trace.

## Local validation and provisioning

Run the credential-free contract check from the repository root:

```bash
python3 infra/tests/validate_contract.py
```

When Azure CLI is available, compile and preview the template without deploying:

```bash
az bicep build --file infra/main.bicep
az deployment group what-if \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --template-file infra/main.bicep \
  --parameters @infra/parameters/dev.json \
  imageTag="$GIT_SHA" \
  postgresAdminPassword="$POSTGRES_ADMIN_PASSWORD"
```

`postgresAdminPassword` is a secure parameter and is intentionally absent from both checked-in parameter examples. The Key Vault secrets `workplace-db` and `consent-signing-key` must be created through the secret-management process before the app revision is made customer-facing.

## Configuration mapping

| Local contract | Azure source |
| --- | --- |
| `ConnectionStrings__WorkplaceDb` | Key Vault secret `workplace-db`, referenced by Container Apps managed identity |
| `Onboarding__ConsentSigningKey` | Key Vault secret `consent-signing-key`, referenced by Container Apps managed identity |
| `AzureAd__ClientId` / `AzureAd__Audience` | Selected development or production Entra parameters |
| `Onboarding__PublicBaseUrl` | First exact allowed ingress hostname with `https://` |
| `Onboarding__ConsentRedirectUri` | First exact redirect URI from the selected Entra registration |
| `ASPNETCORE_URLS` | `http://+:8080` behind controlled HTTPS ingress |
| `GET /health` | Startup, readiness and liveness probes |

The same Dockerfile builds the frontend and publishes the API in both profiles. Only managed services and configuration sources differ; the API still uses the existing PostgreSQL migrations and port 8080 contract.

## Deployment stages

`.github/workflows/validate-and-deploy.yml` runs build/test, dependency and security checks, local infrastructure validation, Bicep compilation, immutable image push, non-production what-if, the approved migration job, revision deployment, a `/health` smoke test, and traffic routing as the final step.

The migration job is deliberately an explicit operational gate. It must use the approved migration image/command for the deployed application version and complete successfully before the revision is routed. The current API applies migrations only in its Development startup profile, so production migration command ownership must be agreed before enabling a production pipeline environment; this foundation does not claim production readiness by itself.

## Secret rotation

Rotate `workplace-db` and `consent-signing-key` in Key Vault, wait for the new version to be available, then restart or redeploy the affected revision. Do not put secret values in GitHub Actions parameters, Bicep files, Container App plain environment values, or log output. The managed identity needs only the Key Vault Secrets User role assigned by the module.

## Rollback

Container Apps runs in multiple revision mode. If a smoke test or post-deploy check fails, leave customer traffic on the previous revision (the workflow routes traffic only after smoke success). For an already-routed revision, route 100% back to the last known-good revision with `az containerapp ingress traffic set`, then investigate logs before retrying the immutable image tag.

## Migration procedure

1. Build and scan the exact immutable image tag.
2. Run the non-production what-if and confirm only expected changes.
3. Start the approved migration job and wait for successful completion.
4. Deploy the revision with the same image tag.
5. Smoke-test `/health` on the new revision hostname.
6. Route traffic and verify application, audit and alert signals.

Never run an unreviewed destructive SQL script as part of deployment. PostgreSQL is private-networked through the shared VNet and delegated subnet.

## Alert ownership and log retention

The Atea platform operations owner owns Container Apps availability, revision failures, Key Vault reference failures, PostgreSQL health and deployment alerts. The application owner owns API authorization, Graph consent/PIM and data-isolation alerts. Log Analytics retention is 30 days for dev and 90 days for prod; Application Insights is workspace-based. Extend retention only through an approved data-retention decision.

No Azure deployment or live test-tenant validation is performed by the local validation command. A deployed URL and real-tenant evidence are still required before production readiness can be claimed.
