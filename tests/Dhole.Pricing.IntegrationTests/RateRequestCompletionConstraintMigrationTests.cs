using Dhole.Pricing.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestCompletionConstraintMigrationTests
{
    [TestMethod]
    public void PreparedSellerAmendmentMayCompleteBeforeEmailIsQueued()
    {
        var migration = new AllowPreparedSellerModificationPendingDelivery();
        var sql = string.Join("\n", migration.UpOperations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));

        StringAssert.Contains(sql, "DROP CONSTRAINT IF EXISTS ck_rate_requests_modification_requires_delivery");
        StringAssert.Contains(sql, "ADD CONSTRAINT ck_rate_requests_modification_requires_prepared_revision");
        StringAssert.Contains(sql, "payload_json ->> 'kind'");
        StringAssert.Contains(sql, "status <> 'Completed'");
        StringAssert.Contains(sql, "rate_id IS NOT NULL");
        StringAssert.Contains(sql, "prepared_revision_number > 0");
        StringAssert.Contains(sql, "completed_at_utc IS NOT NULL");
        // A pending email is no longer a forbidden Completed state. The retry
        // worker will later set delivery_confirmed_at_utc when enqueued.
        Assert.IsFalse(sql.Contains("AND delivery_confirmed_at_utc IS NOT NULL",
            StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(sql, "NOT VALID");
    }
}
