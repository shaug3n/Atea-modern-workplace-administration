#!/usr/bin/env python3
"""Check first-release traffic in the compiled application ARM template."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: validate_compiled_application.py <compiled-application.json>")
        return 2

    template = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    deployments = [resource for resource in template["resources"] if resource["type"] == "Microsoft.Resources/deployments"]
    apps = [
        resource
        for deployment in deployments
        for resource in deployment["properties"]["template"]["resources"]
        if resource["type"] == "Microsoft.App/containerApps"
    ]
    if len(apps) != 1:
        print(f"Expected one compiled Container App, found {len(apps)}.")
        return 1

    traffic = apps[0]["properties"]["configuration"]["ingress"].get("traffic")
    if not isinstance(traffic, list) or len(traffic) != 1:
        print("First release must route 100% to its only revision; traffic rules are missing or ambiguous.")
        return 1
    rule = traffic[0]
    if rule.get("latestRevision") is not True or type(rule.get("weight")) is not int or rule["weight"] != 100:
        print("First release must route exactly 100% to the latest revision.")
        return 1

    print("Compiled first-release traffic contract passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
