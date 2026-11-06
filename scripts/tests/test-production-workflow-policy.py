#!/usr/bin/env python3
"""Verify master-triggered Pricing releases retain safety and FTL image guards."""
import re
from pathlib import Path
workflow = Path('.github/workflows/deploy-production.yml').read_text(encoding='utf-8')
trigger = workflow.split('\npermissions:', 1)[0]
assert re.search(r'(?m)^on:\s*$', trigger), 'Missing workflow trigger'
assert re.search(r'(?m)^  push:\s*\n    branches: \[master\]', trigger), 'Production must trigger on every master push'
assert not re.search(r'(?m)^    paths:', trigger), 'Production cannot filter out deployment commits'
assert 'confirm_production_deploy:' not in trigger, 'No manual confirmation input should be required'
assert "if: github.event_name == 'workflow_dispatch'" not in workflow, 'No job-level manual gate'
assert 'Verify production image FTL mapping before container replacement' in workflow
assert workflow.index('Verify production image FTL mapping before container replacement') < workflow.index('Deploy Pricing')
assert 'Validate configured Dynamics key against active template before redeploy' in workflow
assert 'Verify EF migrations are synchronized' in workflow
assert 'Verify Pricing API health' in workflow
assert 'cancel-in-progress: false' in workflow
assert 'Keep only master and develop branches' not in workflow
print('PASS: automatic master deployment with Dynamics/FTL/migration/health and rollback guards.')
