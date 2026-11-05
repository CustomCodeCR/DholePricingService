#!/usr/bin/env python3
"""Fail closed when production Pricing workflow could deploy without manual approval."""
import re
from pathlib import Path

workflow = Path(".github/workflows/deploy-production.yml").read_text(encoding="utf-8")
trigger = workflow.split("\npermissions:", 1)[0]
assert re.search(r"(?m)^on:\s*$", trigger), "Missing workflow trigger"
assert re.search(r"(?m)^  workflow_dispatch:\s*$", trigger), "Manual workflow_dispatch is required"
assert not re.search(r"(?m)^  (push|pull_request|schedule|workflow_run|repository_dispatch):", trigger), (
    "Production must not deploy on non-manual events"
)
assert "confirm_production_deploy:" in trigger, "Manual approval input required"
assert "type: boolean" in trigger and "default: false" in trigger
assert 'if [ "$GITHUB_REF" != "refs/heads/master" ] || [ "$CONFIRM_PRODUCTION" != "true" ]; then' in workflow
assert "Verify production image FTL mapping before container replacement" in workflow
assert workflow.index("Confirm explicit production authorization") < workflow.index("Build images")
assert workflow.index("Verify production image FTL mapping before container replacement") < workflow.index("Deploy Pricing")
assert "cancel-in-progress: false" in workflow, "Do not interrupt in-flight releases"
assert "Keep only master and develop branches" not in workflow, "Do not delete work-in-progress branches"
print("PASS: production requires explicit authorization, master ref and verified FTL mapping.")
