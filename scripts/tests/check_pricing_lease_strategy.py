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
