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

    required_parameters = {
        "location": r"param\s+location\s+string",
        "environment": r"param\s+environment\s+string",
        "imageTag": r"param\s+imageTag\s+string",
        "postgresSkuName": r"param\s+postgresSkuName\s+string",
        "postgresStorageSizeGb": r"param\s+postgresStorageSizeGb\s+int",
        "allowedIngressHostnames": r"param\s+allowedIngressHostnames\s+array",
        "keyVaultName": r"param\s+keyVaultName\s+string",
        "logAnalyticsWorkspaceName": r"param\s+logAnalyticsWorkspaceName\s+string",
        "applicationInsightsName": r"param\s+applicationInsightsName\s+string",
        "entraDevelopmentClientId": r"param\s+entraDevelopmentClientId\s+string",
        "entraDevelopmentAudience": r"param\s+entraDevelopmentAudience\s+string",
        "entraDevelopmentRedirectUris": r"param\s+entraDevelopmentRedirectUris\s+array",
        "entraProductionClientId": r"param\s+entraProductionClientId\s+string",
        "entraProductionAudience": r"param\s+entraProductionAudience\s+string",
        "entraProductionRedirectUris": r"param\s+entraProductionRedirectUris\s+array",
    }
    for name, pattern in required_parameters.items():
        require_text(main_bicep, pattern, f"Bicep parameter {name}", errors)

    for module_name in (
        "container-registry",
        "postgres",
        "key-vault",
        "container-apps",
        "monitoring",
        "identity",
    ):
        require_file(ROOT / "infra" / "modules" / f"{module_name}.bicep", errors)
        require_text(main_bicep, rf"module\s+{re.escape(module_name.replace('-', ''))}", f"main module {module_name}", errors)

    require_text(main_bicep, r"westeurope", "EU/EEA-safe development region", errors)
    postgres_bicep = require_file(ROOT / "infra" / "modules" / "postgres.bicep", errors)
    require_text(main_bicep + postgres_bicep, r"publicNetworkAccess\s*:\s*'Disabled'", "private PostgreSQL network access", errors)
    require_text(postgres_bicep, r"Microsoft\.App/environments", "Container Apps subnet delegation", errors)
    require_text(main_bicep, r"postgresAdminPassword", "secure PostgreSQL administrator password parameter", errors)

    container_apps = require_file(ROOT / "infra" / "modules" / "container-apps.bicep", errors)
    require_text(container_apps, r"keyVaultUrl", "Key Vault secret reference", errors)
    require_text(container_apps, r"UserAssigned", "managed identity attachment", errors)
    require_text(container_apps, r"allowInsecure\s*:\s*false", "HTTPS-only ingress", errors)
    require_text(container_apps, r"/health", "health probe path", errors)
    require_text(container_apps, r"activeRevisionsMode\s*:\s*'Multiple'", "multiple revision mode", errors)
    require_text(container_apps, r"minReplicas\s*:", "minimum replica bound", errors)
    require_text(container_apps, r"maxReplicas\s*:", "maximum replica bound", errors)
    require_text(container_apps, r"managedCertificates", "managed certificate resource", errors)
    dockerfile = require_file(ROOT / "Dockerfile", errors)
    for build_arg in ("VITE_ENTRA_CLIENT_ID", "VITE_ENTRA_API_SCOPE", "VITE_ENTRA_AUTHORITY", "VITE_ENTRA_REDIRECT_URI"):
        require_text(dockerfile, rf"ARG\s+{build_arg}", f"SPA build argument {build_arg}", errors)
        require_text(workflow, rf"VITE_ENTRA_{build_arg.removeprefix('VITE_ENTRA_')}", f"workflow SPA build value {build_arg}", errors)

    for parameter_path in (
        ROOT / "infra" / "parameters" / "dev.json",
        ROOT / "infra" / "parameters" / "prod.example.json",
    ):
        parameters = load_parameters(parameter_path, errors)
        for name in ("location", "environment", "imageTag", "postgresSkuName", "postgresStorageSizeGb", "allowedIngressHostnames", "keyVaultName", "logAnalyticsWorkspaceName", "applicationInsightsName"):
            if name not in parameters:
                errors.append(f"{parameter_path.relative_to(ROOT)} missing parameter value: {name}")
        for name in ("entraDevelopmentRedirectUris", "entraProductionRedirectUris"):
            for uri in parameter_value(parameters, name) or []:
                if "*" in uri:
                    errors.append(f"wildcard redirect URI is forbidden in {parameter_path.relative_to(ROOT)}: {uri}")
        serialized = json.dumps(parameters)
        for forbidden in ("postgresAdminPassword", "ConnectionStrings__WorkplaceDb", "ConsentSigningKey", "consent-signing-key"):
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

    for label, pattern in {
        "provisioning prerequisites": r"Provisioning prerequisites",
        "secret rotation": r"Secret rotation",
        "rollback": r"Rollback",
        "migration procedure": r"Migration procedure",
        "alert ownership": r"Alert ownership",
        "log retention": r"[Ll]og retention",
        "local/Azure mapping": r"Local.*Azure|Azure.*local",
        "Key Vault bootstrap ordering": r"Bootstrap.*Key Vault|seed.*workplace-db",
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
    print("- delivery contract: build/test, security scan, immutable push, what-if, migration, smoke test, traffic routing")
    return 0


if __name__ == "__main__":
    sys.exit(main())
