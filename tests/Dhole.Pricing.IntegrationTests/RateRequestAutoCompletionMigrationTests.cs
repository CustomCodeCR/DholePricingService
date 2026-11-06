using Dhole.Pricing.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestAutoCompletionMigrationTests
{
    [TestMethod]
    public void LegacyMigrationExplainsThePrematureRevisionTrigger()
    {
        var legacy = new AutoCompleteSellerModificationOnRevisionAdvance();
        var sql = string.Join("\n", legacy.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        StringAssert.Contains(sql, "trg_complete_seller_modification_on_revision");
    }

    [TestMethod]
    public void NewMigrationDropsTheTriggerAndNeverRecreatesIt()
    {
        var migration = new RequireExplicitSellerModificationClosure();
        var sql = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        StringAssert.Contains(sql, "DROP TRIGGER IF EXISTS trg_complete_seller_modification_on_revision");
        StringAssert.Contains(sql, "DROP FUNCTION IF EXISTS pricing.complete_seller_modification_on_revision()");
        Assert.IsFalse(sql.Contains("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void OnlyUnauditedCancelledModificationsAreReopened()
    {
        var migration = new RequireExplicitSellerModificationClosure();
        var sql = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(x => x.Sql));
        StringAssert.Contains(sql, "status = 'Cancelled'");
        StringAssert.Contains(sql, "payload_json ->> 'kind' = 'Modification'");
        StringAssert.Contains(sql, "cancelled_at_utc IS NULL");
        StringAssert.Contains(sql, "cancelled_by_user_id IS NULL");
        StringAssert.Contains(sql, "cancellation_reason IS NULL");
    }
}
