"""Check deployment CLI calls against the installed Azure CLI command contract."""

import os
from pathlib import Path
import re
import shlex
import subprocess
import tempfile
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


class ReleaseCliTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
