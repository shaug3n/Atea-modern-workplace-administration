"""Check deployment CLI calls against the installed Azure CLI command contract."""

import os
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
import textwrap
import unittest


WORKFLOW = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "validate-and-deploy.yml"


def candidate_update_arguments():
    lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
    for index, line in enumerate(lines):
        if re.match(r"\s*az containerapp update\b", line):
            command = line.strip()
            while command.endswith("\\"):
                index += 1
                command = command[:-1] + " " + lines[index].strip()
            return shlex.split(command)
    raise AssertionError("candidate deployment must update the Container App image")


def workflow_step_script(name):
    lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
    marker = f"      - name: {name}"
    start = lines.index(marker)
    run = next(index for index in range(start + 1, len(lines)) if lines[index] == "        run: |")
    body = []
    for line in lines[run + 1:]:
        if line.startswith("      - ") or (line and not line.startswith("          ")):
            break
        body.append(line)
    return textwrap.dedent("\n".join(body))


class ReleaseCliTests(unittest.TestCase):
    def test_build_argument_uses_pull_request_head_branch_and_non_pr_ref_name(self):
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertRegex(
            workflow,
            r"(?m)^\s*ATEA_BUILD_BRANCH: \$\{\{ github\.event_name == 'pull_request' && github\.event\.pull_request\.head\.ref \|\| github\.ref_name \}\}$",
        )

        script = workflow_step_script("Build the immutable hosted image")
        self.assertIn('--build-arg ATEA_BUILD_BRANCH="$ATEA_BUILD_BRANCH"', script)
        self.assertIn('--build-arg ATEA_BUILD_COMMIT="$GITHUB_SHA"', script)

    def test_candidate_update_uses_supported_azure_cli_options(self):
        command = candidate_update_arguments()
        self.assertEqual(command[:3], ["az", "containerapp", "update"])
        for required in ("--name", "--resource-group", "--image", "--revision-suffix"):
            self.assertIn(required, command)

        with tempfile.TemporaryDirectory(prefix="atea-release-cli-") as azure_config:
            env = {**os.environ, "AZURE_CONFIG_DIR": azure_config}
            result = subprocess.run(command[:3] + ["--help"], env=env, capture_output=True, text=True, check=False)

        self.assertEqual(result.returncode, 0, result.stderr)
        supported = set(re.findall(r"(?<!\w)--[a-z][a-z-]*", result.stdout))
        unsupported = [argument for argument in command if argument.startswith("--") and argument not in supported]
        self.assertEqual(unsupported, [], f"Unsupported az containerapp update options: {unsupported}")

    def test_repeat_dispatch_uses_new_revision_and_reassigns_existing_candidate_label(self):
        script = workflow_step_script("Deploy candidate revision after migration")
        script = script.replace("${{ steps.current.outputs.first }}", "false")
        script = script.replace("${{ steps.current.outputs.previous }}", "atea-workplace-dev--4f5a5388b6b1-1")
        fake_az = r'''az() {
          case "$1 $2 $3" in
            "containerapp update "*)
              if [[ " $* " == *" --revision-suffix 35995fc242b8-1 "* ]]; then
                echo 'revision suffix already exists' >&2
                return 1
              fi
              [[ " $* " == *" --revision-suffix 35995fc242b8-36399999999-1 "* ]] || return 2
              ;;
            "containerapp show "*)
              if [[ " $* " == *" properties.latestRevisionName "* ]]; then
                echo 'atea-workplace-dev--35995fc242b8-36399999999-1'
              else
                echo 'atea-workplace-dev--35995fc242b8-36399999999-1.example.test'
              fi
              ;;
            "containerapp ingress traffic") return 0 ;;
            "containerapp revision label")
              [[ " $* " == *" --yes "* ]] || { echo 'NoTTYException' >&2; return 3; }
              ;;
            "containerapp env show") echo 'example.test' ;;
            *) echo "Unexpected Azure command: $*" >&2; return 4 ;;
          esac
        }
        '''
        with tempfile.TemporaryDirectory(prefix="atea-release-retry-") as temp:
            output = Path(temp) / "output"
            summary = Path(temp) / "summary"
            env = {
                **os.environ,
                "AZURE_CONTAINER_APP_NAME": "atea-workplace-dev",
                "AZURE_RESOURCE_GROUP": "test-rg",
                "AZURE_ACR_NAME": "testregistry",
                "AZURE_CA_ENVIRONMENT_NAME": "test-env",
                "IMAGE_REPOSITORY": "atea-unified-workplace",
                "IMAGE_TAG": "35995fc242b8c2641f7873202ad049d56add4c69",
                "GITHUB_RUN_ID": "36399999999",
                "GITHUB_RUN_ATTEMPT": "1",
                "GITHUB_OUTPUT": str(output),
                "GITHUB_STEP_SUMMARY": str(summary),
            }
            result = subprocess.run(
                ["bash", "-e", "-c", fake_az + script],
                env=env, capture_output=True, text=True, check=False,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("revision=atea-workplace-dev--35995fc242b8-36399999999-1", output.read_text())
            self.assertIn("fqdn=atea-workplace-dev---candidate.example.test", output.read_text())


if __name__ == "__main__":
    unittest.main()
