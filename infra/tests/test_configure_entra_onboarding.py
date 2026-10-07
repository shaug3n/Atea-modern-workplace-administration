"""Offline tests for the consent-first registration convergence script."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "infra" / "scripts" / "configure-entra-onboarding.py"
MANIFEST = ROOT / "infra" / "entra" / "delegated-permissions.json"
HOME_TENANT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
API_APP_ID = "11111111-1111-1111-1111-111111111111"
SPA_APP_ID = "22222222-2222-2222-2222-222222222222"
API_SCOPE_ID = "33333333-3333-3333-3333-333333333333"
GRAPH_APP_ID = "00000003-0000-0000-c000-000000000000"
SIGN_IN_URI = "https://workplace.example/auth/callback"
CONSENT_URI = "https://workplace.example/onboarding/consent/callback"


def fake_state() -> dict:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    scopes = [item["name"] for item in manifest["requiredResourceAccess"][0]["resourceAccess"]]
    return {
        "api": {
            "id": "api-object-id",
            "appId": API_APP_ID,
            "signInAudience": "AzureADMyOrg",
            "knownClientApplications": ["aaaaaaaa-0000-0000-0000-000000000001"],
            "identifierUris": ["api://old-api-uri"],
            "requiredResourceAccess": [
                {"resourceAppId": GRAPH_APP_ID, "resourceAccess": []},
                {"resourceAppId": "44444444-4444-4444-4444-444444444444", "resourceAccess": [{"id": "external", "type": "Role"}]},
            ],
            "web": {"redirectUris": ["https://workplace.example/legacy-api-consent"]},
            "spa": {"redirectUris": []},
        },
        "spa": {
            "id": "spa-object-id",
            "appId": SPA_APP_ID,
            "signInAudience": "AzureADMyOrg",
            "knownClientApplications": [],
            "requiredResourceAccess": [
                {
                    "resourceAppId": API_APP_ID,
                    "resourceAccess": [
                        {"id": API_SCOPE_ID, "type": "Scope"},
                        {"id": "unrelated", "type": "Scope"},
                    ],
                },
                {"resourceAppId": "55555555-5555-5555-5555-555555555555", "resourceAccess": [{"id": "other", "type": "Scope"}]},
            ],
            "spa": {"redirectUris": ["https://workplace.example/old-spa-callback"]},
            "web": {"redirectUris": ["https://workplace.example/old-web-callback"]},
        },
        "graphSp": {
            "id": "graph-sp-id",
            "appId": GRAPH_APP_ID,
            "oauth2PermissionScopes": [
                {"id": f"{index + 1:08d}-0000-0000-0000-000000000000", "value": scope, "isEnabled": True}
                for index, scope in enumerate(scopes)
            ],
        },
        "apiSp": {
            "id": "api-sp-id",
            "appId": API_APP_ID,
            "oauth2PermissionScopes": [
                {"id": API_SCOPE_ID, "value": "access_as_user", "isEnabled": True},
                {"id": "66666666-6666-6666-6666-666666666666", "value": "platform.admin", "isEnabled": True},
            ],
        },
        "patches": [],
        "staleSpaReadAfterPatch": False,
    }


def load_script():
    spec = importlib.util.spec_from_file_location("configure_entra_onboarding", SCRIPT)
    if spec is None or spec.loader is None:
        raise AssertionError("could not load registration script")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@unittest.skipUnless(SCRIPT.is_file(), "registration script not implemented yet")
class RegistrationScriptBehaviorTests(unittest.TestCase):
    def setUp(self):
        self.module = load_script()
        self.state = fake_state()

    def arguments(self, *extra: str) -> list[str]:
        return [
            "--expected-home-tenant-id", HOME_TENANT_ID,
            "--api-app-id", API_APP_ID,
            "--customer-spa-app-id", SPA_APP_ID,
            "--api-application-id-uri", f"api://{API_APP_ID}",
            "--sign-in-redirect-uri", SIGN_IN_URI,
            "--consent-redirect-uri", CONSENT_URI,
            *extra,
        ]

    def run_with_fake_az(self, *extra: str) -> tuple[int, str, str]:
        module = self.module
        state = self.state

        def fake_run(command, *, input=None, capture_output=False, text=False, check=False):
            if command[:3] == ["az", "account", "show"]:
                return subprocess.CompletedProcess(command, 0, HOME_TENANT_ID + "\n", "")
            if command[:2] != ["az", "rest"]:
                return subprocess.CompletedProcess(command, 1, "", "unexpected Azure CLI command")
            method = command[command.index("--method") + 1]
            url = command[command.index("--url") + 1]
            if method == "GET":
                if "/applications?" in url:
                    item = state["api"] if API_APP_ID in url else state["spa"]
                    if state["staleSpaReadAfterPatch"] and item is state["spa"] and state["patches"]:
                        item = dict(item, signInAudience="AzureADMyOrg")
                    return subprocess.CompletedProcess(command, 0, json.dumps({"value": [item]}), "")
                if "/servicePrincipals?" in url:
                    item = state["graphSp"] if GRAPH_APP_ID in url else state["apiSp"]
                    return subprocess.CompletedProcess(command, 0, json.dumps({"value": [item]}), "")
                return subprocess.CompletedProcess(command, 1, "", "unexpected GET URL")
            if method == "PATCH":
                object_id = url.rsplit("/", 1)[-1]
                patch_body = json.loads(command[command.index("--body") + 1])
                item = state["api"] if object_id == "api-object-id" else state["spa"]
                item.update(patch_body)
                state["patches"].append((object_id, patch_body))
                return subprocess.CompletedProcess(command, 0, "", "")
            return subprocess.CompletedProcess(command, 1, "", "unexpected HTTP method")

        stdout = io.StringIO()
        stderr = io.StringIO()
        with patch.object(module.subprocess, "run", side_effect=fake_run), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            code = module.main(self.arguments(*extra))
        return code, stdout.getvalue(), stderr.getvalue()

    def test_default_is_sanitized_dry_run_and_apply_is_explicit(self):
        code, stdout, stderr = self.run_with_fake_az()

        self.assertEqual(code, 0, stderr)
        self.assertIn("dry-run", stdout.lower())
        self.assertIn("signInAudience", stdout)
        self.assertNotIn("clientSecret", stdout)
        self.assertNotIn("admin consent granted", stdout.lower())
        self.assertEqual(self.state["patches"], [])

    def test_apply_converges_and_preserves_unrelated_resources_and_redirects(self):
        code, _, stderr = self.run_with_fake_az("--apply")

        self.assertEqual(code, 0, stderr)
        self.assertEqual(len(self.state["patches"]), 2)
        api = self.state["api"]
        spa = self.state["spa"]
        self.assertEqual(api["signInAudience"], "AzureADMultipleOrgs")
        self.assertIn(SPA_APP_ID, api["knownClientApplications"])
        self.assertIn("aaaaaaaa-0000-0000-0000-000000000001", api["knownClientApplications"])
        self.assertIn(f"api://{API_APP_ID}", api["identifierUris"])
        self.assertIn("api://old-api-uri", api["identifierUris"])
        self.assertIn("https://workplace.example/legacy-api-consent", api["web"]["redirectUris"])
        self.assertIn("44444444-4444-4444-4444-444444444444", [item["resourceAppId"] for item in api["requiredResourceAccess"]])
        graph = next(item for item in api["requiredResourceAccess"] if item["resourceAppId"] == GRAPH_APP_ID)
        self.assertEqual({item["type"] for item in graph["resourceAccess"]}, {"Scope"})
        self.assertEqual(spa["signInAudience"], "AzureADMultipleOrgs")
        self.assertEqual(spa["spa"]["redirectUris"], ["https://workplace.example/old-spa-callback", SIGN_IN_URI])
        self.assertEqual(spa["web"]["redirectUris"], ["https://workplace.example/old-web-callback", CONSENT_URI])
        self.assertIn("55555555-5555-5555-5555-555555555555", [item["resourceAppId"] for item in spa["requiredResourceAccess"]])
        api_access = next(item for item in spa["requiredResourceAccess"] if item["resourceAppId"] == API_APP_ID)
        self.assertEqual(api_access["resourceAccess"], [{"id": API_SCOPE_ID, "type": "Scope"}])

        second_code, _, second_stderr = self.run_with_fake_az("--apply")

        self.assertEqual(second_code, 0, second_stderr)
        self.assertEqual(len(self.state["patches"]), 2)

    def test_wrong_home_tenant_fails_before_any_patch(self):
        self.module
        with patch.object(self.module.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\n", "")):
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr):
                code = self.module.main(self.arguments("--apply"))

        self.assertNotEqual(code, 0)
        self.assertIn("tenant", stderr.getvalue().lower())
        self.assertEqual(self.state["patches"], [])

    def test_manifest_scopes_must_be_enabled_and_unambiguous(self):
        original = self.state["graphSp"]["oauth2PermissionScopes"][0]
        for scopes in (
            [scope for scope in self.state["graphSp"]["oauth2PermissionScopes"] if scope is not original],
            [{**scope, "isEnabled": False} if scope is original else scope for scope in self.state["graphSp"]["oauth2PermissionScopes"]],
            [*self.state["graphSp"]["oauth2PermissionScopes"], dict(original)],
        ):
            with self.subTest(scope_count=len(scopes)):
                self.state["graphSp"]["oauth2PermissionScopes"] = scopes
                code, _, stderr = self.run_with_fake_az("--apply")
                self.assertNotEqual(code, 0)
                self.assertIn("scope", stderr.lower())
                self.assertEqual(self.state["patches"], [])
                self.state = fake_state()

    def test_unexpected_graph_application_permission_fails_closed(self):
        self.state["api"]["requiredResourceAccess"][0]["resourceAccess"] = [{"id": "app-role", "type": "Role"}]

        code, _, stderr = self.run_with_fake_az("--apply")

        self.assertNotEqual(code, 0)
        self.assertIn("application permission", stderr.lower())
        self.assertEqual(self.state["patches"], [])

    def test_customer_spa_graph_or_platform_admin_permission_is_refused(self):
        self.state["spa"]["requiredResourceAccess"].append(
            {"resourceAppId": GRAPH_APP_ID, "resourceAccess": [{"id": "graph", "type": "Scope"}]}
        )
        code, _, stderr = self.run_with_fake_az("--apply")
        self.assertNotEqual(code, 0)
        self.assertIn("customer", stderr.lower())
        self.assertEqual(self.state["patches"], [])

        self.state = fake_state()
        self.state["spa"]["requiredResourceAccess"][0]["resourceAccess"].append(
            {"id": "66666666-6666-6666-6666-666666666666", "type": "Scope"}
        )
        code, _, stderr = self.run_with_fake_az("--apply")
        self.assertNotEqual(code, 0)
        self.assertIn("platform.admin", stderr.lower())
        self.assertEqual(self.state["patches"], [])

    def test_post_write_reread_failure_is_reported(self):
        self.state["staleSpaReadAfterPatch"] = True

        code, _, stderr = self.run_with_fake_az("--apply")

        self.assertNotEqual(code, 0)
        self.assertIn("verification", stderr.lower())
        self.assertGreater(len(self.state["patches"]), 0)


class RegistrationScriptExistenceTests(unittest.TestCase):
    def test_registration_configuration_script_exists(self):
        self.assertTrue(SCRIPT.is_file(), "expected infra/scripts/configure-entra-onboarding.py")


if __name__ == "__main__":
    unittest.main()
