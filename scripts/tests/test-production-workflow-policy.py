#!/usr/bin/env python3
"""Verify Pricing production runs only on an explicit dispatch from master."""
import re
from pathlib import Path

workflow = Path(".github/workflows/deploy-production.yml").read_text(encoding="utf-8")
trigger = workflow.split("\npermissions:", 1)[0]
assert re.search(r"(?m)^on:\s*$", trigger), "Missing workflow trigger"
assert re.search(r"(?m)^  workflow_dispatch:\s*$", trigger), "Explicit Run workflow is required"
assert not re.search(r"(?m)^  (push|pull_request|schedule|workflow_run|repository_dispatch):", trigger), (
    "Production must never deploy on an ordinary push or non-manual event"
)
assert "confirm_production_deploy" not in workflow, "Redundant confirmation input must not return"
assert "CONFIRM_PRODUCTION" not in workflow, "Redundant approval gate must not return"
assert "Verify master release branch" in workflow
assert 'if [ "$GITHUB_REF" != "refs/heads/master" ]; then' in workflow
assert "Verify production image FTL mapping before container replacement" in workflow
assert workflow.index("Verify master release branch") < workflow.index("Build images")
assert workflow.index("Verify production image FTL mapping before container replacement") < workflow.index("Deploy Pricing")
assert "cancel-in-progress: false" in workflow, "Do not interrupt in-flight releases"
assert "Keep only master and develop branches" not in workflow, "Do not delete work-in-progress branches"
print("PASS: one-step production dispatch, master restriction, safe image verification.")
