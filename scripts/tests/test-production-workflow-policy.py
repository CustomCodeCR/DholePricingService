#!/usr/bin/env python3
"""Guard automatic Pricing production release with branch, preflight and health checks."""
import re
from pathlib import Path

workflow = Path(".github/workflows/deploy-production.yml").read_text(encoding="utf-8")
trigger = workflow.split("\npermissions:", 1)[0]
assert re.search(r"(?m)^on:\s*$", trigger), "Missing workflow trigger"
assert re.search(r"(?m)^  push:\s*\n    branches: \[master\]", trigger), "Production must auto-run on master push"
assert not re.search(r"(?m)^  (pull_request|schedule|workflow_run|repository_dispatch):", trigger), "Unapproved trigger"
assert "confirm_production_deploy" not in workflow and "CONFIRM_PRODUCTION" not in workflow, "No manual approval should remain"
assert 'test "$GITHUB_REF" = "refs/heads/master"' in workflow, "Must fail closed outside master"
assert "Verify production image FTL mapping before container replacement" in workflow, "FTL mapping preflight required"
assert "Validate configured Dynamics key against active template before redeploy" in workflow, "Dynamics key preflight required"
assert "Verify EF migrations are synchronized" in workflow, "Pending migrations check required"
assert workflow.index("Verify production image FTL mapping before container replacement") < workflow.index("Deploy Pricing"), "FTL check must precede deployment"
assert "Verify Pricing API health" in workflow, "Post-deploy health check required"
assert "cancel-in-progress: false" in workflow, "Do not interrupt in-flight production rollout"
print("PASS: Pricing production automatically triggers on master and retains release safety checks.")
