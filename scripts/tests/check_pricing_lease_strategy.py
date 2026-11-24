#!/usr/bin/env python3
"""Structural guard: retry strategy must enclose each lease transaction."""
from pathlib import Path
import re

source = Path("src/Dhole.Pricing.Api/Endpoints/RateRequestLeaseEndpoints.cs").read_text(encoding="utf-8")
for name in ("ClaimCoreAsync", "HeartbeatAsync", "ReleaseOwnAsync"):
    match = re.search(r"private static async Task<IResult> " + name + r"\(", source)
    assert match, f"Missing {name}"
    following = source.find("\n    private static ", match.end())
    body = source[match.start():following if following != -1 else None]
    strategy = body.find("ExecuteLeaseTransactionAsync(db, async () =>")
    transaction = body.find("BeginTransactionAsync(ct)")
    assert 0 <= strategy < transaction, f"Transaction outside retry strategy: {name}"
    assert body.count("BeginTransactionAsync(ct)") == 1, f"Unexpected transaction count in {name}"
assert "db.Database.CreateExecutionStrategy().ExecuteAsync(operation)" in source
print("PASS: Claim, Heartbeat and ReleaseOwn use EF Core's Npgsql retry strategy.")


approval = Path("src/Dhole.Pricing.Api/Endpoints/ExpiredRateUpdateApprovalEndpoints.cs").read_text(encoding="utf-8")
for name in ("RequestAsync", "DecideAsync"):
    match = re.search(r"private static async Task<IResult> " + name + r"\(", approval)
    assert match, f"Missing {name}"
    following = approval.find("\n    private static ", match.end())
    body = approval[match.start():following if following != -1 else None]
    strategy = body.find("ExecuteApprovalTransactionAsync(db, async isRetry =>")
    transaction = body.find("BeginTransactionAsync(ct)")
    assert 0 <= strategy < transaction, f"Approval transaction outside retry strategy: {name}"
    assert body.count("BeginTransactionAsync(ct)") == 1
    assert "if (isRetry)" in body, f"Missing ambiguous-commit reconciliation: {name}"
    assert body.index("await db.SaveChangesAsync(ct)") < body.index("await transaction.CommitAsync(ct)")
assert "db.Database.CreateExecutionStrategy().ExecuteAsync" in approval
assert "if (isRetry) db.ChangeTracker.Clear();" in approval
print("PASS: expiry request/decision transactions use EF retry strategy and commit outbox atomically.")
