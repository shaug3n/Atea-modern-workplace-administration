#!/usr/bin/env python3
"""Converge existing Atea-owned app registrations for consent-first onboarding."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys
from urllib.parse import quote, urlsplit
from uuid import UUID


GRAPH_APP_ID = "00000003-0000-0000-c000-000000000000"
GRAPH_ROOT = "https://graph.microsoft.com/v1.0"
DEFAULT_MANIFEST = Path(__file__).resolve().parents[1] / "entra" / "delegated-permissions.json"


class ConfigurationError(Exception):
    pass


def canonical_guid(value: str, label: str) -> str:
    try:
        parsed = UUID(value)
    except (ValueError, AttributeError):
        raise ConfigurationError(f"{label} must be a GUID.") from None
    if parsed.int == 0:
        raise ConfigurationError(f"{label} must not be an empty GUID.")
    return str(parsed)


def validate_redirect(value: str, expected_path: str, label: str) -> str:
    parsed = urlsplit(value)
    if (
        parsed.scheme not in ("https", "http")
        or not parsed.hostname
        or parsed.username is not None
        or parsed.password is not None
        or parsed.query
        or parsed.fragment
        or parsed.path != expected_path
        or (parsed.scheme == "http" and parsed.hostname not in ("localhost", "127.0.0.1", "::1"))
    ):
        raise ConfigurationError(f"{label} must be an exact secure redirect URI ending in {expected_path}.")
    return value


def validate_application_uri(value: str) -> str:
    parsed = urlsplit(value)
    if not parsed.scheme or not parsed.netloc or parsed.username is not None or parsed.password is not None or parsed.query or parsed.fragment:
        raise ConfigurationError("API application-ID URI must be an absolute URI without credentials, query, or fragment.")
    return value.rstrip("/")


def run_az(arguments: list[str], input_text: str | None = None) -> str:
    try:
        result = subprocess.run(
            ["az", *arguments],
            input=input_text,
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError:
        raise ConfigurationError("Azure CLI is unavailable.") from None
    if result.returncode != 0:
        raise ConfigurationError(f"Azure CLI command failed (exit {result.returncode}); no raw CLI output was included.")
    return result.stdout


def get_json(url: str) -> dict:
    output = run_az(["rest", "--method", "GET", "--url", url])
    try:
        return json.loads(output)
    except json.JSONDecodeError:
        raise ConfigurationError("Microsoft Graph returned invalid JSON.") from None


def get_one(resource: str, app_id: str, select: str) -> dict:
    url = f"{GRAPH_ROOT}/{resource}?$filter=appId%20eq%20'{quote(app_id)}'&$select={quote(select, safe=',')}"
    response = get_json(url)
    items = response.get("value") if isinstance(response, dict) else None
    if not isinstance(items, list) or len(items) != 1 or not isinstance(items[0], dict):
        raise ConfigurationError(f"Expected exactly one existing {resource[:-1]} for the supplied app ID.")
    return items[0]


def load_manifest(path: Path) -> list[str]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        raise ConfigurationError("Delegated-permission manifest is missing or invalid JSON.") from None
    blocks = document.get("requiredResourceAccess") if isinstance(document, dict) else None
    if (
        not isinstance(blocks, list)
        or len(blocks) != 1
        or not isinstance(blocks[0], dict)
        or not isinstance(blocks[0].get("resourceAppId"), str)
        or blocks[0]["resourceAppId"].lower() != GRAPH_APP_ID
    ):
        raise ConfigurationError("Manifest must contain exactly one Microsoft Graph resource block.")
    permissions = blocks[0].get("resourceAccess")
    if not isinstance(permissions, list) or not permissions:
        raise ConfigurationError("Manifest has no Graph delegated permissions.")
    names: list[str] = []
    for permission in permissions:
        if not isinstance(permission, dict) or permission.get("type") != "Scope" or not isinstance(permission.get("name"), str):
            raise ConfigurationError("Manifest may contain only named Graph delegated scopes.")
        names.append(permission["name"])
    if len(names) != len(set(names)) or names != sorted(names):
        raise ConfigurationError("Manifest scope names must be distinct and ordinal-sorted.")
    return names


def enabled_scope_ids(service_principal: dict, scope_name: str) -> list[str]:
    scopes = service_principal.get("oauth2PermissionScopes", [])
    if not isinstance(scopes, list):
        return []
    return [
        str(scope.get("id"))
        for scope in scopes
        if isinstance(scope, dict) and scope.get("value") == scope_name and scope.get("isEnabled") is True and scope.get("id")
    ]


def scope_ids(service_principal: dict, scope_name: str) -> list[str]:
    scopes = service_principal.get("oauth2PermissionScopes", [])
    if not isinstance(scopes, list):
        return []
    return [
        str(scope.get("id"))
        for scope in scopes
        if isinstance(scope, dict) and scope.get("value") == scope_name and scope.get("id")
    ]


def resolve_scope_ids(service_principal: dict, names: list[str], label: str) -> dict[str, str]:
    resolved: dict[str, str] = {}
    for name in names:
        matches = enabled_scope_ids(service_principal, name)
        if len(matches) != 1:
            raise ConfigurationError(f"{label} scope {name} is missing, disabled, or ambiguous.")
        resolved[name] = matches[0]
    return resolved


def merge_resource_block(resources: list[dict], app_id: str, permissions: list[dict]) -> list[dict]:
    if any(not isinstance(resource, dict) for resource in resources):
        raise ConfigurationError("Existing requiredResourceAccess contains an invalid resource block.")
    result = []
    replacement = {"resourceAppId": app_id, "resourceAccess": permissions}
    replaced = False
    for resource in resources:
        if str(resource.get("resourceAppId", "")).lower() == app_id.lower():
            if not replaced:
                result.append(replacement)
                replaced = True
        else:
            result.append(resource)
    if not replaced:
        result.append(replacement)
    return result


def merge_redirects(configuration: dict, key: str, uri: str) -> dict:
    result = dict(configuration) if isinstance(configuration, dict) else {}
    redirects = result.get("redirectUris", [])
    if not isinstance(redirects, list):
        raise ConfigurationError(f"Existing {key} redirectUris must be an array.")
    if uri not in redirects:
        result["redirectUris"] = [*redirects, uri]
    return result


def build_changes(api: dict, spa: dict, api_sp: dict, graph_sp: dict, scope_names: list[str], args) -> tuple[dict, dict]:
    graph_permissions = next(
        (item.get("resourceAccess", []) for item in api.get("requiredResourceAccess", [])
         if str(item.get("resourceAppId", "")).lower() == GRAPH_APP_ID),
        [],
    )
    if any(isinstance(permission, dict) and permission.get("type") == "Role" for permission in graph_permissions):
        raise ConfigurationError("API registration has unexpected Graph application permissions; clean them up manually.")

    spa_resources = spa.get("requiredResourceAccess", [])
    if not isinstance(spa_resources, list):
        raise ConfigurationError("Customer SPA requiredResourceAccess must be an array.")
    if any(
        str(resource.get("resourceAppId", "")).lower() == GRAPH_APP_ID and resource.get("resourceAccess")
        for resource in spa_resources
    ):
        raise ConfigurationError("Customer SPA has direct Graph permissions; clean them up manually.")

    api_scope_map = resolve_scope_ids(api_sp, ["access_as_user"], "API")
    platform_scope_ids = {scope_id.lower() for scope_id in scope_ids(api_sp, "platform.admin")}
    customer_api_block = next(
        (resource for resource in spa_resources if str(resource.get("resourceAppId", "")).lower() == args.api_app_id.lower()),
        {"resourceAccess": []},
    )
    if any(
        str(permission.get("id", "")).lower() in platform_scope_ids
        for permission in customer_api_block.get("resourceAccess", [])
        if isinstance(permission, dict)
    ):
        raise ConfigurationError("Customer SPA includes platform.admin; remove that permission manually.")

    graph_scope_ids = resolve_scope_ids(graph_sp, scope_names, "Microsoft Graph")
    api_resources = api.get("requiredResourceAccess", [])
    if not isinstance(api_resources, list):
        raise ConfigurationError("API requiredResourceAccess must be an array.")
    api_access = merge_resource_block(
        api_resources,
        GRAPH_APP_ID,
        [{"id": graph_scope_ids[name], "type": "Scope"} for name in scope_names],
    )
    known_clients = api.get("knownClientApplications", [])
    if not isinstance(known_clients, list):
        raise ConfigurationError("API knownClientApplications must be an array.")
    if not any(str(item).lower() == args.customer_spa_app_id.lower() for item in known_clients):
        known_clients = [*known_clients, args.customer_spa_app_id]
    identifier_uris = api.get("identifierUris", [])
    if not isinstance(identifier_uris, list) or any(not isinstance(item, str) for item in identifier_uris):
        raise ConfigurationError("API identifierUris must be an array of strings.")
    if args.api_application_id_uri not in identifier_uris:
        identifier_uris = [*identifier_uris, args.api_application_id_uri]

    spa_api_access = merge_resource_block(
        spa_resources,
        args.api_app_id,
        [{"id": api_scope_map["access_as_user"], "type": "Scope"}],
    )
    api_patch = {
        "signInAudience": "AzureADMultipleOrgs",
        "knownClientApplications": known_clients,
        "requiredResourceAccess": api_access,
        "identifierUris": sorted(set(identifier_uris)),
    }
    spa_patch = {
        "signInAudience": "AzureADMultipleOrgs",
        "requiredResourceAccess": spa_api_access,
        "spa": merge_redirects(spa.get("spa", {}), "spa", args.sign_in_redirect_uri),
        "web": merge_redirects(spa.get("web", {}), "web", args.consent_redirect_uri),
    }
    return api_patch, spa_patch


def patch_if_changed(item: dict, patch: dict) -> dict:
    return {key: value for key, value in patch.items() if item.get(key) != value}


def apply_patch(object_id: str, patch: dict) -> None:
    url = f"{GRAPH_ROOT}/applications/{quote(object_id)}"
    run_az(
        [
            "rest",
            "--method", "PATCH",
            "--url", url,
            "--headers", "Content-Type=application/json",
            "--body", json.dumps(patch, separators=(",", ":")),
        ],
    )


def verify(expected: dict, actual: dict, fields: tuple[str, ...]) -> bool:
    return all(actual.get(field) == expected.get(field) for field in fields)


def configure(args) -> int:
    expected_tenant = canonical_guid(args.expected_home_tenant_id, "Expected home tenant ID")
    api_app_id = canonical_guid(args.api_app_id, "API app ID")
    spa_app_id = canonical_guid(args.customer_spa_app_id, "Customer SPA app ID")
    if api_app_id == spa_app_id:
        raise ConfigurationError("API and customer SPA app IDs must be different.")
    application_uri = validate_application_uri(args.api_application_id_uri)
    sign_in_uri = validate_redirect(args.sign_in_redirect_uri, "/auth/callback", "Sign-in redirect URI")
    consent_uri = validate_redirect(args.consent_redirect_uri, "/onboarding/consent/callback", "Consent redirect URI")
    if urlsplit(sign_in_uri).netloc.lower() != urlsplit(consent_uri).netloc.lower() or urlsplit(sign_in_uri).scheme != urlsplit(consent_uri).scheme:
        raise ConfigurationError("Sign-in and consent redirects must use the same exact origin.")
    args.api_app_id = api_app_id
    args.customer_spa_app_id = spa_app_id
    args.api_application_id_uri = application_uri
    args.sign_in_redirect_uri = sign_in_uri
    args.consent_redirect_uri = consent_uri

    tenant = run_az(["account", "show", "--query", "tenantId", "-o", "tsv"]).strip()
    if tenant.lower() != expected_tenant:
        raise ConfigurationError("Selected Azure CLI tenant does not match the expected home tenant.")

    scope_names = load_manifest(args.manifest)
    api = get_one("applications", api_app_id, "id,appId,signInAudience,knownClientApplications,requiredResourceAccess,identifierUris,web,spa")
    spa = get_one("applications", spa_app_id, "id,appId,signInAudience,knownClientApplications,requiredResourceAccess,web,spa")
    graph_sp = get_one("servicePrincipals", GRAPH_APP_ID, "id,appId,oauth2PermissionScopes")
    api_sp = get_one("servicePrincipals", api_app_id, "id,appId,oauth2PermissionScopes")
    if not all(item.get("id") for item in (api, spa, graph_sp, api_sp)):
        raise ConfigurationError("Required existing application/service principal is missing.")

    api_patch, spa_patch = build_changes(api, spa, api_sp, graph_sp, scope_names, args)
    api_changes = patch_if_changed(api, api_patch)
    spa_changes = patch_if_changed(spa, spa_patch)
    summary = {
        "mode": "apply" if args.apply else "dry-run",
        "registrations": [
            {
                "appId": api_app_id,
                "changes": {key: {"before": api.get(key), "after": value} for key, value in api_changes.items()},
            },
            {
                "appId": spa_app_id,
                "changes": {key: {"before": spa.get(key), "after": value} for key, value in spa_changes.items()},
            },
        ],
    }
    if not args.apply:
        print(json.dumps(summary, indent=2))
        return 0
    if not api_changes and not spa_changes:
        print(json.dumps({"mode": "apply", "result": "already converged"}, indent=2))
        return 0

    write_error = None
    try:
        if api_changes:
            apply_patch(api["id"], api_changes)
        if spa_changes:
            apply_patch(spa["id"], spa_changes)
    except ConfigurationError as exc:
        write_error = exc

    try:
        api_after = get_one("applications", api_app_id, "id,appId,signInAudience,knownClientApplications,requiredResourceAccess,identifierUris,web,spa")
        spa_after = get_one("applications", spa_app_id, "id,appId,signInAudience,knownClientApplications,requiredResourceAccess,web,spa")
    except ConfigurationError:
        if write_error is not None:
            raise ConfigurationError("Registration write failed and post-write state could not be verified; inspect both applications.") from None
        raise
    if not verify(api_patch, api_after, tuple(api_patch)) or not verify(spa_patch, spa_after, tuple(spa_patch)):
        raise ConfigurationError("Post-write registration verification failed; inspect both existing applications for partial updates.")
    if write_error is not None:
        raise ConfigurationError("Azure CLI reported a write failure; post-write state matched but requires operator review.") from None
    print(json.dumps({"mode": "apply", "result": "verified", "updatedRegistrations": int(bool(api_changes)) + int(bool(spa_changes))}, indent=2))
    return 0


def parse_args(argv: list[str] | None = None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected-home-tenant-id", required=True)
    parser.add_argument("--api-app-id", required=True)
    parser.add_argument("--customer-spa-app-id", required=True)
    parser.add_argument("--api-application-id-uri", required=True)
    parser.add_argument("--sign-in-redirect-uri", required=True)
    parser.add_argument("--consent-redirect-uri", required=True)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--apply", action="store_true", help="write the reviewed changes; default is sanitized dry-run")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        return configure(parse_args(argv))
    except ConfigurationError as exc:
        print(f"configuration error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
