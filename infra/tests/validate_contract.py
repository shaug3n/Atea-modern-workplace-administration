#!/usr/bin/env python3
"""Validate the local Azure deployment contract without Azure credentials."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def require_file(path: Path, errors: list[str]) -> str:
    if not path.is_file():
        errors.append(f"missing required file: {path.relative_to(ROOT)}")
        return ""
    return path.read_text(encoding="utf-8")


def require_text(text: str, pattern: str, label: str, errors: list[str]) -> None:
    if not re.search(pattern, text, flags=re.MULTILINE):
        errors.append(f"missing {label}: /{pattern}/")


def load_parameters(path: Path, errors: list[str]) -> dict:
    if not path.is_file():
        errors.append(f"missing parameter file: {path.relative_to(ROOT)}")
        return {}
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        errors.append(f"invalid JSON in {path.relative_to(ROOT)}: {exc}")
        return {}
    parameters = document.get("parameters")
    if not isinstance(parameters, dict):
        errors.append(f"{path.relative_to(ROOT)} must contain a parameters object")
        return {}
    return parameters


def parameter_value(parameters: dict, name: str):
    value = parameters.get(name, {}).get("value")
    return value


def main() -> int:
    errors: list[str] = []
    main_bicep = require_file(ROOT / "infra" / "main.bicep", errors)
    workflow = require_file(ROOT / ".github" / "workflows" / "validate-and-deploy.yml", errors)
    operations = require_file(ROOT / "docs" / "operations" / "azure-deployment.md", errors)
    readme = require_file(ROOT / "README.md", errors)

    application_bicep = require_file(ROOT / "infra" / "application.bicep", errors)
    database_bootstrap = require_file(ROOT / "infra" / "database-bootstrap.bicep", errors)
    bootstrap_dockerfile = require_file(ROOT / "infra" / "scripts" / "db-bootstrap.Dockerfile", errors)
    for name in ("containerAppEnvironmentName", "containerAppsStorageAccountName", "containerRegistryName", "keyVaultName", "postgresServerName", "virtualNetworkName"):
        require_text(main_bicep, rf"param\s+{name}\s+string", f"foundation parameter {name}", errors)
    for module_name in ("container-registry", "postgres", "key-vault", "monitoring", "identity", "data-protection-storage", "container-app-environment"):
        require_file(ROOT / "infra" / "modules" / f"{module_name}.bicep", errors)
    for module_name in ("identity", "registry", "keyVault", "monitoring", "postgres", "dataProtection", "containerAppsEnvironment"):
        require_text(main_bicep, rf"module\s+{module_name}\s+", f"foundation module {module_name}", errors)
    require_text(application_bicep, r"param\s+publicBaseUrl\s+string", "application public URL parameter", errors)
    require_text(application_bicep, r"param\s+allowedIngressHostnames\s+array\s*=\s*\[\]", "optional custom hostnames", errors)
    require_text(application_bicep, r"consentRedirectUri", "separate consent redirect URI", errors)
    require_text(application_bicep, r"customerRedirectUri", "separate customer redirect URI", errors)
    require_text(application_bicep, r"platformAdminRedirectUri", "separate admin redirect URI", errors)
    if re.search(r"imageTag|entraApi|RedirectUri|api-client-secret|workplace-db|workplace-migration-db", main_bicep, re.IGNORECASE):
        errors.append("foundation template must not depend on image, app secrets, Entra registrations, or callbacks")

    require_text(main_bicep, r"westeurope", "EU/EEA-safe development region", errors)
    postgres_bicep = require_file(ROOT / "infra" / "modules" / "postgres.bicep", errors)
    require_text(main_bicep + postgres_bicep, r"publicNetworkAccess\s*:\s*'Disabled'", "private PostgreSQL network access", errors)
    require_text(postgres_bicep, r"Microsoft\.App/environments", "Container Apps subnet delegation", errors)
    require_text(main_bicep, r"postgresAdminPassword", "secure PostgreSQL administrator password parameter", errors)
    storage_bicep = require_file(ROOT / "infra" / "modules" / "data-protection-storage.bicep", errors)
    require_text(storage_bicep, r"allowSharedKeyAccess:\s*false", "shared-key-disabled key-ring storage", errors)
    require_text(storage_bicep, r"defaultAction:\s*'Deny'", "network-restricted key-ring storage", errors)
    require_text(bootstrap_dockerfile, r"bootstrap-postgres-roles\.sql", "versioned private database-role bootstrap image", errors)
    require_text(database_bootstrap, r"Microsoft\.App/jobs@2024-03-01", "private-network database bootstrap job", errors)
    require_text(database_bootstrap, r"postgres-bootstrap-admin-password", "temporary administrator password secret", errors)
    require_text(database_bootstrap, r"postgres-bootstrap-migration-password", "temporary migration password secret", errors)
    require_text(database_bootstrap, r"postgres-bootstrap-runtime-password", "temporary runtime password secret", errors)
    require_text(database_bootstrap, r"PGSSLMODE", "TLS-required private database bootstrap connection", errors)
    if re.search(r"ingress\s*:", database_bootstrap):
        errors.append("database bootstrap job must not define ingress")

    container_apps = require_file(ROOT / "infra" / "modules" / "container-apps.bicep", errors)
    migration_runner = require_file(ROOT / "src" / "Api" / "Infrastructure" / "Persistence" / "DatabaseMigrationRunner.cs", errors)
    program = require_file(ROOT / "src" / "Api" / "Program.cs", errors)
    migration_sql = require_file(ROOT / "infra" / "scripts" / "bootstrap-postgres-roles.sql", errors)
    require_text(migration_runner, r'GetConnectionString\("WorkplaceMigrationDb"\)', "separate migration database connection", errors)
    require_text(migration_runner, r"MigrateAsync", "EF migration runner", errors)
    require_text(program, r'args\[0\].*--migrate', "finite --migrate command dispatch", errors)
    require_text(program, r"IsDevelopment\(\)[\s\S]{0,400}MigrateAsync", "Development-only automatic migration", errors)
    require_text(migration_sql, r"workplace_migrator", "dedicated migrator database role", errors)
    require_text(migration_sql, r"workplace_runtime", "restricted runtime database role", errors)
    require_text(migration_sql, r"REVOKE CREATE ON SCHEMA public FROM PUBLIC", "runtime schema DDL restriction", errors)
    require_text(container_apps, r"keyVaultUrl", "Key Vault secret reference", errors)
    require_text(container_apps, r"UserAssigned", "managed identity attachment", errors)
    require_text(container_apps, r"allowInsecure\s*:\s*false", "HTTPS-only ingress", errors)
    require_text(container_apps, r"ipSecurityRestrictions:\s*\[[\s\S]{0,400}action:\s*'Allow'", "allowlisted-only application ingress", errors)
    require_text(container_apps, r"/health", "health probe path", errors)
    require_text(container_apps, r"/health/ready", "database readiness probe path", errors)
    require_text(container_apps, r"ASPNETCORE_ENVIRONMENT[\s\S]{0,90}Staging", "non-Development Azure test environment", errors)
    for secret_name in ("api-client-secret", "workplace-db", "workplace-migration-db", "consent-signing-key", "continuation-signing-key"):
        require_text(container_apps, rf"secrets/{re.escape(secret_name)}", f"Key Vault secret reference {secret_name}", errors)
    for config_name in ("HostedAuth__CustomerRedirectUri", "HostedAuth__PlatformAdminRedirectUri", "PlatformAuthorization__HomeTenantId", "DataProtection__BlobUri", "DataProtection__KeyIdentifier", "DataProtection__ManagedIdentityClientId"):
        require_text(container_apps, re.escape(config_name), f"hosted configuration {config_name}", errors)
    key_vault = require_file(ROOT / "infra" / "modules" / "key-vault.bicep", errors)
    require_text(key_vault, r"enablePurgeProtection:\s*true", "irreversible Key Vault purge protection on all environments", errors)
    require_text(key_vault, r"workplace-data-protection", "Key Vault Data Protection wrapping key", errors)
    require_text(key_vault, r"12338af0-0e69-4776-bea7-57ae8d297424", "least-privilege Key Vault Crypto User assignment", errors)
    require_text(key_vault, r"provisionPrincipalObjectId", "provision-only Key Vault Secrets Officer principal", errors)
    require_text(key_vault, r"b86a8fe4-44ce-4948-aee5-eccb2c155cd7", "provision-only Key Vault Secrets Officer assignment", errors)
    if "deploymentPrincipalObjectId" in key_vault:
        errors.append("routine deployment identity must not receive Key Vault access")
    registry = require_file(ROOT / "infra" / "modules" / "container-registry.bicep", errors)
    if re.search(r"trustPolicy\s*:", registry):
        errors.append("new registries must not enable deprecated Docker Content Trust")
    require_text(registry, r"properties:\s*environment\s*==\s*'prod'\s*\?", "Premium-only registry policies excluded from Basic dev registry", errors)
    require_text(container_apps, r"activeRevisionsMode\s*:\s*'Multiple'", "multiple revision mode", errors)
    require_text(container_apps, r"substring\(toLower\(imageTag\),\s*0,\s*12\)", "bounded first-revision suffix", errors)
    require_text(container_apps, r"minReplicas\s*:", "minimum replica bound", errors)
    require_text(container_apps, r"maxReplicas\s*:", "maximum replica bound", errors)
    require_text(container_apps, r"managedCertificates", "managed certificate resource", errors)
    migration_job = container_apps.split("resource databaseMigrationJob", 1)[-1]
    for label, pattern in {
        "manual migration job": r"Microsoft\.App/jobs@2024-03-01",
        "migration command": r"Atea\.UnifiedWorkplace\.Api\.dll', '--migrate",
        "same immutable job image": r"\$\{registryLoginServer\}/\$\{imageRepository\}:\$\{imageTag\}",
        "job private Container Apps environment": r"environmentId:\s*managedEnvironment\.id",
        "single migration replica": r"parallelism:\s*1",
        "bounded job timeout": r"replicaTimeout:\s*900",
        "no automatic migration retries": r"replicaRetryLimit:\s*0",
        "separate migration connection secret": r"ConnectionStrings__WorkplaceMigrationDb",
    }.items():
        require_text(migration_job, pattern, label, errors)
    if re.search(r"ingress\s*:", migration_job):
        errors.append("migration job must not define ingress")
    dockerfile = require_file(ROOT / "Dockerfile", errors)
    api_project = require_file(ROOT / "src" / "Api" / "Atea.UnifiedWorkplace.Api.csproj", errors)
    unit_tests = require_file(ROOT / "tests" / "Api.UnitTests" / "Api.UnitTests.csproj", errors)
    integration_tests = require_file(ROOT / "tests" / "Api.IntegrationTests" / "Api.IntegrationTests.csproj", errors)
    global_json = require_file(ROOT / "global.json", errors)
    for label, project in (
        ("API", api_project),
        ("unit tests", unit_tests),
        ("integration tests", integration_tests),
    ):
        require_text(project, r"<TargetFramework>net10\.0</TargetFramework>", f"{label} targeting net10.0", errors)
    require_text(api_project, r'Include="Microsoft\.EntityFrameworkCore\.(?:Design|Tools)" Version="10\.0\.12"', "EF Core tooling 10.0.12", errors)
    require_text(api_project, r'Include="Npgsql\.EntityFrameworkCore\.PostgreSQL" Version="10\.0\.3"', "Npgsql EF provider 10.0.3", errors)
    require_text(api_project, r'Include="Microsoft\.Identity\.Web" Version="4\.15\.0"', "Microsoft.Identity.Web 4.15.0", errors)
    require_text(api_project, r'Include="Azure\.Extensions\.AspNetCore\.DataProtection\.Blobs" Version="1\.5\.4"', "Azure Blob Data Protection key store", errors)
    require_text(api_project, r'Include="Azure\.Extensions\.AspNetCore\.DataProtection\.Keys" Version="1\.6\.4"', "Azure Key Vault Data Protection key wrapping", errors)
    require_text(dockerfile, r"FROM mcr\.microsoft\.com/dotnet/sdk:10\.0\.401 AS api-build", ".NET SDK container 10.0.401", errors)
    require_text(dockerfile, r"FROM mcr\.microsoft\.com/dotnet/aspnet:10\.0\.12 AS final", ".NET ASP.NET runtime container 10.0.12", errors)
    require_text(workflow, r"dotnet-version:\s*'10\.0\.401'", "CI .NET SDK 10.0.401", errors)
    require_text(workflow, r"revision label add[\s\S]{0,300}--label candidate", "stable candidate revision label", errors)
    require_text(workflow, r"AZURE_SMOKE_TEST_SOURCE_CIDR", "test-only ingress operator allowlist", errors)
    require_text(workflow, r"ingress access-restriction set", "temporary GitHub runner ingress allowlist", errors)
    require_text(workflow, r"--revision-suffix \"\$\{IMAGE_TAG:0:12\}-\$GITHUB_RUN_ATTEMPT\"", "bounded unique subsequent revision suffix", errors)
    require_text(workflow, r'\$\{AZURE_CONTAINER_APP_NAME\}---candidate\.\$\{default_domain\}', "candidate label FQDN", errors)
    require_text(workflow, r"verify the workspace, then approve the test-promotion environment", "explicit interactive candidate smoke gate", errors)
    require_text(global_json, r'"version"\s*:\s*"10\.0\.401"', "global .NET SDK pin 10.0.401", errors)
    require_text(workflow, r"dotnet test tests/Api.UnitTests/Api\.UnitTests\.csproj --configuration Release", "CI API unit test project", errors)
    require_text(workflow, r"dotnet test tests/Api.IntegrationTests/Api\.IntegrationTests\.csproj --configuration Release", "CI API integration test project", errors)
    require_text(workflow, r"dotnet package list[\s\S]{0,160}--vulnerable[\s\S]{0,80}--format json", "machine-readable NuGet vulnerability audit", errors)
    require_text(workflow, r"npm run test:behavior --prefix src/Web -- --run", "CI frontend behavior tests", errors)
    require_text(workflow, r"npm run build --prefix src/Web", "CI frontend production build", errors)
    if re.search(r"dotnet test\s+Atea\.UnifiedWorkplace\.sln\b", workflow):
        errors.append("CI must invoke the API test projects explicitly instead of relying on solution configuration mappings")
    for build_arg in ("VITE_ENTRA_CLIENT_ID", "VITE_ENTRA_API_SCOPE", "VITE_ENTRA_AUTHORITY", "VITE_ENTRA_REDIRECT_URI"):
        require_text(dockerfile, rf"ARG\s+{build_arg}", f"SPA build argument {build_arg}", errors)
        require_text(workflow, rf"VITE_ENTRA_{build_arg.removeprefix('VITE_ENTRA_')}", f"workflow SPA build value {build_arg}", errors)
    for build_arg in ("VITE_PLATFORM_ADMIN_CLIENT_ID", "VITE_PLATFORM_ADMIN_AUTHORITY", "VITE_PLATFORM_ADMIN_SCOPE", "VITE_PLATFORM_ADMIN_REDIRECT_URI"):
        require_text(dockerfile, rf"ARG\s+{build_arg}", f"hosted admin SPA build argument {build_arg}", errors)
        require_text(workflow, re.escape(build_arg), f"workflow hosted admin SPA build value {build_arg}", errors)

    for parameter_path in (ROOT / "infra" / "parameters" / "dev.json", ROOT / "infra" / "parameters" / "prod.example.json"):
        parameters = load_parameters(parameter_path, errors)
        for name in ("location", "environment", "postgresSkuName", "postgresStorageSizeGb", "keyVaultName", "logAnalyticsWorkspaceName", "applicationInsightsName", "containerAppsStorageAccountName"):
            if name not in parameters:
                errors.append(f"{parameter_path.relative_to(ROOT)} missing parameter value: {name}")
        serialized = json.dumps(parameters)
        for forbidden in ("postgresAdminPassword", "ConnectionStrings__WorkplaceDb", "ConsentSigningKey", "consent-signing-key", "api-client-secret"):
            if forbidden in serialized:
                errors.append(f"secret-bearing value/name must not be committed in {parameter_path.relative_to(ROOT)}: {forbidden}")

    for parameter_path in (ROOT / "infra" / "parameters" / "application-dev.example.json", ROOT / "infra" / "parameters" / "application-prod.example.json"):
        parameters = load_parameters(parameter_path, errors)
        for name in ("environment", "imageTag", "publicBaseUrl", "smokeTestSourceCidr", "consentRedirectUri", "customerRedirectUri", "platformAdminRedirectUri", "allowedIngressHostnames"):
            if name not in parameters:
                errors.append(f"{parameter_path.relative_to(ROOT)} missing parameter value: {name}")
        for name in ("consentRedirectUri", "customerRedirectUri", "platformAdminRedirectUri", "publicBaseUrl"):
            uri = parameter_value(parameters, name)
            if not isinstance(uri, str) or not uri.startswith("https://") or "*" in uri:
                errors.append(f"{parameter_path.relative_to(ROOT)} requires exact HTTPS host configuration: {name}")
        callbacks = [parameter_value(parameters, name) for name in ("consentRedirectUri", "customerRedirectUri", "platformAdminRedirectUri")]
        if len(set(callbacks)) != 3:
            errors.append(f"{parameter_path.relative_to(ROOT)} must use three distinct callback URIs")
        serialized = json.dumps(parameters)
        for forbidden in ("api-client-secret", "workplace-db", "workplace-migration-db", "ConnectionStrings__"):
            if forbidden in serialized:
                errors.append(f"secret-bearing value/name must not be committed in {parameter_path.relative_to(ROOT)}: {forbidden}")

    for label, pattern in {
        "build/test workflow stage": r"build-test",
        "dependency/security workflow stage": r"dependency-security",
        "immutable container tag": r"github\.sha",
        "Bicep what-if": r"what-if",
        "database migration gate": r"migration",
        "revision deployment": r"revision",
        "smoke test": r"smoke",
        "traffic routing after smoke": r"traffic",
    }.items():
        require_text(workflow, pattern, label, errors)
    require_text(workflow, r"branches:\s*\[master\]", "master branch CI trigger", errors)
    require_text(workflow, r"concurrency:[\s\S]{0,200}group:", "serialized deployment gate", errors)
    require_text(workflow, r"environment:\s*test", "protected test deployment environment", errors)
    require_text(workflow, r"template-file\s+infra/application\.bicep", "application-only release deployment", errors)
    if "template-file infra/main.bicep" in workflow:
        errors.append("release workflow must not redeploy the foundation on every application release")
    migration_position = workflow.find("Database migration job")
    revision_position = workflow.find("Deploy candidate revision")
    smoke_position = workflow.find("Smoke test candidate")
    route_position = workflow.find("Route traffic after smoke")
    if not (0 <= migration_position < revision_position < smoke_position < route_position):
        errors.append("release must migrate before candidate deployment and route only after candidate smoke succeeds")
    for public_value in ("VITE_ENTRA_CLIENT_ID", "VITE_ENTRA_API_SCOPE", "VITE_ENTRA_AUTHORITY", "VITE_ENTRA_REDIRECT_URI", "VITE_PLATFORM_ADMIN_CLIENT_ID", "VITE_PLATFORM_ADMIN_AUTHORITY", "VITE_PLATFORM_ADMIN_SCOPE", "VITE_PLATFORM_ADMIN_REDIRECT_URI"):
        require_text(workflow, re.escape(public_value), f"public SPA build value {public_value}", errors)
    provision = require_file(ROOT / ".github" / "workflows" / "provision-test.yml", errors)
    require_text(provision, r"workflow_dispatch", "manual-only foundation workflow", errors)
    require_text(provision, r"template-file\s+infra/main\.bicep", "foundation deployment command", errors)
    require_text(provision, r"infra/database-bootstrap\.bicep", "private database-role bootstrap deployment", errors)
    require_text(provision, r"workplace-migration-db", "seeded migration connection string", errors)
    require_text(provision, r"postgres-bootstrap-runtime-password", "temporary bootstrap credentials", errors)
    require_text(provision, r'deploymentPrincipalObjectId="\$AZURE_DEPLOYMENT_PRINCIPAL_OBJECT_ID"', "ACR push role assignment for deploy identity", errors)
    require_text(provision, r'provisionPrincipalObjectId="\$AZURE_PROVISION_PRINCIPAL_OBJECT_ID"', "separate provision identity role assignments", errors)
    require_text(provision, r"AZURE_PROVISION_CLIENT_ID", "federated provision identity", errors)
    require_text(workflow, r"AZURE_DEPLOY_CLIENT_ID", "separate federated release identity", errors)
    if "az keyvault secret show" in workflow:
        errors.append("routine release workflow must not read Key Vault secrets; readiness checks verify references without exposing their values")
    if re.search(r"branches:\s*\[main\]", workflow):
        errors.append("CI workflow must follow master instead of main")

    for label, pattern in {
        "provisioning prerequisites": r"Prerequisites",
        "secret rotation": r"Secret rotation",
        "rollback": r"Rollback",
        "migration procedure": r"Rollback and migrations",
        "alert ownership": r"Alert ownership",
        "log retention": r"workspace retains",
        "local/Azure mapping": r"Local.*Azure|Azure.*local",
        "Key Vault bootstrap ordering": r"Key Vault first|Key Vault.*bootstrap|Key Vault secret",
    }.items():
        require_text(operations, pattern, label, errors)

    require_text(readme, r"infra/tests/validate_contract\.py", "README infrastructure validation command", errors)
    require_text(readme, r"azure-deployment\.md", "README Azure operations runbook link", errors)

    if errors:
        print("Infrastructure contract validation failed:")
        for error in errors:
            print(f"- {error}")
        return 1

    print("Infrastructure contract validation passed.")
    print("- parameter contract: region, environment, image tag, PostgreSQL sizing, ingress, Key Vault, monitoring, Entra separation")
    print("- security contract: no wildcard redirects and no committed secret values")
    print("- runtime contract: managed identity, Key Vault references, HTTPS ingress, /health probes, bounded revisions")
    print("- delivery contract: build/test, security scan, immutable push, what-if, migration, restricted candidate smoke test, traffic routing")
    return 0


if __name__ == "__main__":
    sys.exit(main())
