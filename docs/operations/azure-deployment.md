# Azure deployment runbook

This runbook deploys the application to a disposable personal Azure test subscription. It does not deploy to Atea's tenant or make the service customer-ready. Wait for the Daybreak security review and resolve its findings before inviting external customers or enabling privileged operations.

The deployment is staged: an operator provisions a private foundation and database roles, configures Entra and GitHub, then dispatches a signed image release. Atea production requires a separate approval, subscription, Entra registration, secrets, backup/restore design, and operational review.

## Before spending credits

1. Confirm the selected Azure subscription and directory are the personal test tenant, not an Atea subscription. The workflows check the subscription and tenant IDs before applying changes.
2. In Azure Portal, create an **empty resource group** in the selected test subscription: **Resource groups → Create**, choose the subscription, enter a name, select the approved region, and select **Review + create**. The dev example uses `westeurope`. Record the exact name as `AZURE_RESOURCE_GROUP`; the GitHub deployment identities receive roles scoped to this existing group. The workflow verifies its location and creates resources *inside* it.
3. Open **Cost Management + Billing → Budgets** and create a monthly budget scoped to the dedicated subscription or resource group, with alerts such as 50%, 75%, and 90% to an inbox you monitor. A budget is an alert, **not** a spending cap; Azure credits do not prevent charges if services outlive those credits. Review current regional pricing and quota availability before continuing.
4. Choose unique Container Registry, Key Vault, storage, and PostgreSQL names in `infra/parameters/dev.json`. Do not deploy the example names unchanged if they are already in use.
5. Keep the workflow inputs explicit. The foundation workflow creates billable resources. Running `what-if` is only a preview; confirming the workflow continues into resource creation.

## Prerequisites

- A personal Azure subscription with enough quota for Azure Container Apps, Container Registry, Key Vault, Storage, Log Analytics, and private Azure Database for PostgreSQL Flexible Server.
- A Microsoft Entra test tenant and (for both customer and platform-admin paths) three app registrations: one API app registration exposing delegated `access_as_user` and `platform.admin` scopes; one customer SPA registration granted `access_as_user`; and a separate Atea platform-admin SPA registration granted `platform.admin`. Configure the API's Microsoft Graph delegated permissions and tenant consent for the modules being tested.
- Two GitHub OIDC identities. The provision identity needs a credential for the GitHub `test` environment; the release identity needs **two** credentials, one for `test` and another for `test-promotion`. Use audience `api://AzureADTokenExchange` for each. Do not create client secrets for GitHub Actions. The provision identity needs Contributor and temporary User Access Administrator at the existing dedicated resource-group scope to create foundation resources and narrowly scoped role assignments. The release identity needs Contributor at that resource group; the foundation grants it ACR Push. The provision identity alone receives Key Vault Secrets Officer on the dedicated test vault so it can seed values; it is not used for ordinary releases. Remove temporary User Access Administrator from the provision identity after foundation setup and retain it only when an approved foundation reprovision is necessary. Keep both identities' access limited to this test resource group.
- Docker, GitHub Actions, and the workflow files from this repository.

In Entra, create both single-tenant GitHub OIDC app registrations/service principals. Under each app registration, open **Certificates & secrets → Federated credentials → Add credential → GitHub Actions deploying Azure resources**; choose the repository owner/name and entity type **Environment**. Add `test` to both apps and `test-promotion` to the release app. For the name-based GitHub OIDC format, the subjects end in `repo:<owner>/<repo>:environment:test` and `repo:<owner>/<repo>:environment:test-promotion`, respectively. Repositories using GitHub's newer immutable subject format include the numeric owner and repository IDs; confirm the generated subject matches the token format your repository uses rather than assuming the name-based form ([GitHub OIDC reference](https://docs.github.com/en/actions/reference/security/oidc)). A personal GitHub account can be the repository owner; if Entra asks for Organization ID or Repository ID, use the numeric `id` fields from GitHub's user and repository API responses, not the Entra tenant ID or `node_id`. Copy each app's **client ID** into the matching GitHub environment secret, and its service principal's **object ID** into the matching repository variable. Assign the provision principal Contributor and temporary User Access Administrator on the disposable resource group; assign the release principal Contributor there. Do not add either identity to the Atea platform-operator allowlist.

The Container Apps candidate revision is reached through a stable revision-label URL that has a unique ACA hostname. Revision labels provide a stable URL pinned to one revision, independent of primary traffic weighting ([Microsoft revision docs](https://learn.microsoft.com/en-us/azure/container-apps/revisions-manage)). Ingress allow rules limit the app to explicitly listed IPv4 ranges ([Microsoft IP restrictions](https://learn.microsoft.com/en-us/azure/container-apps/ip-restrictions)). Add the following **exact** callback URLs to their corresponding Entra SPA registrations before the first application release:

| SPA registration | Primary host callback | Candidate host callback |
| --- | --- | --- |
| Customer SPA | `https://<app>.<environment-domain>/auth/callback` | `https://<app>---candidate.<environment-domain>/auth/callback` |
| Platform admin SPA | `https://<app>.<environment-domain>/admin/auth/callback` | `https://<app>---candidate.<environment-domain>/admin/auth/callback` |

Use the environment's actual `defaultDomain` output, including its unique environment and region components. Do not use wildcard redirect URIs. The SPA computes its sign-in callback from the current origin, which makes candidate-host sign-in return to the candidate revision. The API consent callback remains the primary host at `/onboarding/consent/callback`. If you configure a custom domain, register its exact callbacks as well and validate the ACA certificate/DNS process separately.

## GitHub configuration

Create the `test` environment and protect it with required reviewers for foundation/deployment operations. Create a separate `test-promotion` environment with a required reviewer; approving this job is the explicit human confirmation that the candidate was opened, signed in, and inspected before customer traffic is routed.

Add the non-secret values below as **repository variables**. The `build-test` job has no GitHub Environment, so setting SPA build values only on `test` would silently fall back to placeholders. These repository variables are visible to both protected deploy jobs:

- `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `AZURE_ACR_NAME`, `AZURE_KEY_VAULT_NAME`, `AZURE_CONTAINER_APP_NAME`, `AZURE_CA_ENVIRONMENT_NAME`.
- `AZURE_MIGRATION_JOB_NAME`, exactly `<AZURE_CONTAINER_APP_NAME>-migration`.
- `AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID` and `AZURE_PROVISION_PRINCIPAL_OBJECT_ID`: the OIDC service principals' **object IDs**, used to grant ACR Push and provision-only Key Vault write access.
- `AZURE_APP_PUBLIC_URL`: exact HTTPS primary host, `https://<app>.<environment-domain>` (no trailing slash).
- `AZURE_SMOKE_TEST_SOURCE_CIDR`: your test operator's current public IPv4 CIDR (usually `<address>/32`). Do not use `0.0.0.0/0`. The app's ACA ingress allows only this range; CI temporarily adds its runner egress IP during health probes and removes that rule afterwards. If your ISP/VPN address changes, update the variable and rerun deployment before opening the hosted URL.
- `PLATFORM_HOME_TENANT_ID`, `PLATFORM_ADMIN_OBJECT_IDS_JSON` (a JSON array of Atea/test operator object IDs).
- `ENTRA_API_CLIENT_ID`, `ENTRA_API_AUDIENCE`, `CUSTOMER_SPA_CLIENT_ID`, `CUSTOMER_API_SCOPE`, `CUSTOMER_ENTRA_AUTHORITY`, `PLATFORM_ADMIN_CLIENT_ID`, and `PLATFORM_ADMIN_SCOPE`.

Add these GitHub environment secrets (the `test` environment needs the provision and deploy credentials; `test-promotion` needs only the deploy credential plus tenant/subscription):

- `AZURE_DEPLOY_CLIENT_ID`, `AZURE_PROVISION_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` for OIDC login. Duplicate the deploy client ID and tenant/subscription secrets into both protected GitHub environments (`test` and `test-promotion`); the provision client ID is needed only in `test`. The tenant must match the selected subscription and the platform home tenant used by the test deployment.
- `POSTGRES_ADMIN_PASSWORD`, a unique high-entropy password for the one-time database role bootstrap. This is never passed into the application container.

Never put API client secrets, DB passwords, signing keys, or connection strings in GitHub variables, source files, workflow outputs, or checked-in parameter files. Keep the API app-registration client secret ready to enter directly into Key Vault after the foundation has been provisioned.

## Provision the foundation and database

1. Run locally first: `python3 infra/tests/validate_contract.py`. Compile `infra/main.bicep`, `infra/application.bicep`, and `infra/database-bootstrap.bicep` with `az bicep build`.
2. Confirm the empty resource group exists, the provision identity has the resource-group roles above, and the workflow's `az group create` step has been replaced with a read-only existence check. In GitHub Actions, manually run **Provision test foundation**. Check `confirm_test_subscription` only after verifying the selected disposable test subscription; leave `bootstrap_only` unchecked for a new foundation. The first protected job performs a Bicep what-if and provisions billable resources inside the existing group. A second protected job uses a fresh OIDC login to bootstrap the database. GitHub may request `test` environment approval for each job.
3. The second job uses a short-lived private Container Apps Job to connect to PostgreSQL inside the VNet, create restricted runtime and migration database roles, and put the resulting connection strings/signing keys in Key Vault. It deletes the temporary job and bootstrap-only Key Vault secrets on exit. If the workflow is interrupted, inspect and remove the named `*-db-bootstrap` job and `postgres-bootstrap-*` secrets after diagnosis.
4. In Azure Portal, open the test Key Vault's **Secrets** page and create `api-client-secret` with the API app registration's client secret. The one-time provision identity writes bootstrap values and connection strings; the release identity never reads or writes secret contents. Confirm the other secrets exist: `workplace-db`, `workplace-migration-db`, `consent-signing-key`, and `continuation-signing-key`. The bootstrap job creates the latter four automatically. Secret **names** are fixed; never paste values into GitHub issues or chat. After provisioning, remove User Access Administrator from the provision principal; retain its vault-scoped Secrets Officer assignment only while bootstrap/recovery workflows need it.
5. Retrieve the Container Apps environment's `defaultDomain` and construct the primary and candidate URLs. For example:

   ```bash
   az deployment group show --name workplace-foundation --resource-group "$AZURE_RESOURCE_GROUP" \
     --query properties.outputs.containerAppsDefaultDomain.value -o tsv
   ```

   Set `<app>.<defaultDomain>` as the primary URL and `<app>---candidate.<defaultDomain>` as the stable candidate URL. Finish registering both host callbacks in Entra before releasing the application.

If the foundation deployment succeeded but the bootstrap job failed (for example, ACR role-assignment propagation or an expired OIDC assertion), do **not** rerun the failed workflow or redeploy the foundation. Start a **new** manual **Provision test foundation** run from the current `master` revision with `confirm_test_subscription` checked and `bootstrap_only` checked. This skips Bicep deployment, authenticates anew, verifies the existing `workplace-foundation` deployment and expected resource names, then builds/pushes the image and bootstraps DB roles. The workflow refuses to rotate any existing runtime connection string or signing key and refuses to proceed while a bootstrap job still exists. If either guard fails, inspect the partial state before deciding on recovery; do not delete live secrets. ACR admin access remains disabled.

## Deploy and validate a release

1. Confirm the release app has federated credentials for both `test` and `test-promotion`. **Validate and deploy** then runs automatically: pushes to `master` and PRs from branches in this repository build, test, scan, and deploy a candidate once a reviewer approves the `test` environment; fork PRs never deploy. Promotion to the primary URL is offered only for `master` runs (and manual dispatches on `master`) and needs a second `test-promotion` approval; PR runs stop at the candidate. Approve `test` only after verifying the test subscription, tenant, and absence of customer data. A manual dispatch remains available and additionally requires the `confirm_test_tenant` checkbox. Caveats: a PR candidate runs the database migration job against the shared test database before merge, so an unmerged or abandoned migration still changes that schema; every qualifying PR push requests a new `test` approval and runs stay pending until approved or rejected, which blocks merging if this workflow is a required check.
2. The deploy job verifies the active subscription, Entra tenant, generated app hostname, migration-job name, and required public configuration. The release identity deliberately cannot read Key Vault secret values; missing secret references cause readiness/startup to fail before the candidate can be promoted. It builds and pushes an immutable image tagged by commit SHA.
3. The dedicated migration job runs `dotnet Atea.UnifiedWorkplace.Api.dll --migrate` against the migration-only DB connection. If it fails, candidate deployment stops and existing traffic remains on its current revision. Review logs before retrying; do not bypass a failed migration.
4. The new revision receives the stable `candidate` label. On the **first** release, Azure requires the only revision to have 100% of the primary-host traffic; access remains limited to `AZURE_SMOKE_TEST_SOURCE_CIDR` while migration, automated checks, and human review run. Later releases keep the previous revision on primary traffic and test the new candidate by its label URL. A fresh workflow dispatch uses a new revision suffix even for the same commit, and moves an existing `candidate` label without an interactive prompt; failed zero-traffic revisions may remain active until normal cleanup. Automated checks validate `/health`, `/health/ready`, and that unauthenticated `/api/platform/session` returns 401.
5. Open the candidate URL printed in the GitHub Actions summary **from the network matching `AZURE_SMOKE_TEST_SOURCE_CIDR`**. Sign in with the nominated test-tenant admin, verify the workspace and intended module paths, then approve `test-promotion`. Do not approve if the candidate cannot sign in or complete the smoke checks. Candidate sign-in requires the exact candidate callback URI above.
6. The promotion job pins the primary host to the approved candidate. A post-route readiness failure rolls back to the previous revision when one exists. The first release has no previous revision to roll back to; keep the test IP allowlist in place and disable ingress if the initial deployment must be taken offline.

The workflow's unauthenticated HTTP checks do not prove real Entra, Graph, PIM, consent, or tenant authorization behavior. Perform the human test-tenant check for each relevant flow and record any permissions/consent that still need configuration. The API consent callback uses the primary host; test tenant consent/onboarding separately on the primary URL after the first deployment if its callback flow must be exercised.

## Configuration mapping

| Local configuration | Azure source |
| --- | --- |
| `ConnectionStrings__WorkplaceDb` | Key Vault secret `workplace-db`, read by the workload's managed identity |
| `ConnectionStrings__WorkplaceMigrationDb` | Key Vault secret `workplace-migration-db`, consumed only by the migration job |
| `Onboarding__ConsentSigningKey` | Key Vault secret `consent-signing-key` |
| `Users__ContinuationSigningKey` | Key Vault secret `continuation-signing-key` |
| `AzureAd__ClientSecret` | Key Vault secret `api-client-secret` |
| `AzureAd__ClientId` / `AzureAd__Audience` | API Entra app ID and configured audience |
| `PlatformAuthorization__HomeTenantId` / `__AdminObjectIds__N` | Test platform tenant and explicit operator object IDs |
| `Onboarding__PublicBaseUrl` | Primary public URL |
| `Onboarding__ConsentRedirectUri` | Primary URL plus `/onboarding/consent/callback` |
| `HostedAuth__CustomerRedirectUri` / `HostedAuth__PlatformAdminRedirectUri` | Runtime validation copies of the primary SPA callback URLs |
| `DataProtection__BlobUri` / `DataProtection__KeyIdentifier` | Shared Blob key ring and Key Vault wrapping key, accessed via managed identity |
| `ASPNETCORE_URLS` | `http://+:8080` behind HTTPS ingress |
| `/health` / `/health/ready` | Liveness and bounded database readiness probes; Graph is not part of either |

Both local and Azure use the same Dockerfile, API migrations, and HTTP port. Azure runs in `Staging`, not `Development`, so local password administration is disabled. Only the infrastructure/configuration source changes.

## Secret rotation

Rotate `workplace-db`, `workplace-migration-db`, `api-client-secret`, `consent-signing-key`, or `continuation-signing-key` through Key Vault and the approved operations process. Restart/redeploy affected revisions so references refresh. Do not put values into Bicep parameters, Container Apps plaintext settings, workflow logs, or source. The workload identity has only the secret and Data Protection permissions it needs.

## Rollback and migrations

The application runs in ACA multiple-revision mode. For a previously promoted release, return 100% to the last known-good revision:

```bash
az containerapp ingress traffic set \
  --name "$AZURE_CONTAINER_APP_NAME" \
  --resource-group "$AZURE_RESOURCE_GROUP" \
  --revision-weight "<known-good-revision>=100" "<bad-revision>=0"
```

For subsequent releases, the workflow keeps the current revision on primary traffic until candidate health and human smoke gates pass. The first release has only one revision, so its primary host is operator-IP-restricted rather than unrouted. Database migrations are not automatically reversed by app rollback; migrations must be additive/backward compatible, and restore procedures must be tested before production. Run no unreviewed destructive SQL in a release.

## Moving the test deployment to another subscription

The test deployment cannot be moved between subscriptions with Azure Resource Mover or `az resource move`. Move validation rejects the user-assigned managed identity, the Container Apps environment, the VNet-injected PostgreSQL Flexible Server, and the VNet (subnet service association links). Redeploy instead:

1. Create the same empty resource group in the target subscription (same tenant) and grant the provision and release principals their resource-group roles there.
2. Choose new globally unique names in `infra/parameters/dev.json`. The Key Vault uses purge protection, so its old name stays reserved for the retention period after deletion.
3. Update `AZURE_SUBSCRIPTION_ID` in both GitHub environments (`test`, `test-promotion`) and the `AZURE_ACR_NAME` / `AZURE_KEY_VAULT_NAME` repository variables. The workflows read these values; they contain no hard-coded subscription IDs.
4. Run **Provision test foundation**, then follow the post-provisioning steps above: new `AZURE_APP_PUBLIC_URL`, new Entra redirect URIs, and `api-client-secret` in the new vault. Release with **Validate and deploy**.
5. After the new deployment passes its smoke checks, delete the old resource group(s), remove old role assignments, and remove the old host's Entra redirect URIs.

The PostgreSQL database starts empty after a redeploy. Data protection keys are not carried over.

In October 2026 the test deployment moved from "Azure subscription 1" (`a0789ebf-8e92-43cd-8dac-d7f97250f5d0`) to "Main subscription" (`19b605e1-04e0-44fc-8732-96c12573c4db`), resource group `rg-atea-workplace-test-noe` in `norwayeast`.

## Alert ownership, cost, retention, and cleanup

The platform operator owns Azure availability, revision failures, Key Vault references, PostgreSQL health, and deployment alerts. The application owner owns authn/authz, Graph consent/PIM, audit, and tenant-isolation alerts. The dev Log Analytics workspace retains 30 days; production example uses 90 days. Confirm data retention with the security/privacy owner before real customer use.

Review actual Azure Cost Management charges during the test. Budgets notify but do not stop resources or cap charges. To stop ongoing spend, use Azure Portal to inspect the dedicated resource group and delete the test deployment when no longer needed; this removes all data and is destructive. Export any required demo evidence/backups first and validate deletion targets carefully.

## Production boundary and handoff to Atea

Do not treat a successful test deployment as production-ready. Before any external customer access, wait for the Daybreak security review, remediate findings, complete a formal threat/security review, test backup restore and incident response, define retention/support/on-call, check regional availability and costs, and obtain Atea change approval. For later transfer, create separate Atea-owned subscriptions, workload identities, Entra registrations, Key Vault secrets, storage encryption keys, database, DNS/certificates, and GitHub OIDC environment. Never carry test-tenant credentials or keys into Atea. Import/migrate only explicitly approved test data; validate tenant IDs, audit history, and owner access before opening customer access.

No Azure resource has been deployed by local validation. A live subscription rollout, signed-in browser verification, and Daybreak review remain external steps.
