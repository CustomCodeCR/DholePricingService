using Dhole.Pricing.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Dhole.Pricing.IntegrationTests;

[TestClass]
public sealed class RateRequestAutoCompletionMigrationTests
{
    [TestMethod]
    public void RevisionAdvanceClosesOnlyTheMatchingOpenSellerModification()
    {
        var migration = new AutoCompleteSellerModificationOnRevisionAdvance();
        var sql = string.Join("\n", migration.UpOperations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));

        StringAssert.Contains(sql, "AFTER UPDATE OF revision_number");
        StringAssert.Contains(sql, "WHEN (NEW.revision_number > OLD.revision_number)");
        StringAssert.Contains(sql, "request.rate_id = NEW.id");
        StringAssert.Contains(sql, "request.status = 'Open'");
        StringAssert.Contains(sql, "request.payload_json ->> 'kind' = 'Modification'");
        StringAssert.Contains(sql, "END = OLD.revision_number");
        StringAssert.Contains(sql, "status = 'Completed'");
        StringAssert.Contains(sql, "prepared_revision_number = NEW.revision_number");
        StringAssert.Contains(sql, "completed_at_utc = clock_timestamp()");
        StringAssert.Contains(sql, "lock_token = NULL");
        Assert.IsFalse(sql.Contains("SET delivery_confirmed_at_utc",
            StringComparison.OrdinalIgnoreCase),
            "Completing Pricing's work must not assert that an email was delivered.");
    }

    [TestMethod]
    public void ExistingOpenRequestsRequirePostSubmissionRevisionEvidence()
    {
        var migration = new AutoCompleteSellerModificationOnRevisionAdvance();
        var sql = string.Join("\n", migration.UpOperations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));

        StringAssert.Contains(sql, "UPDATE pricing.\"RateRequests\" request");
        StringAssert.Contains(sql, "quote.is_deleted = false");
        StringAssert.Contains(sql, "FROM pricing.\"RateRevisions\" previous");
        StringAssert.Contains(sql, "previous.\"RevisionNumber\"");
        StringAssert.Contains(sql, "previous.\"CreatedAtUtc\" >= request.requested_at_utc");
        StringAssert.Contains(sql, "quote.revision_number");
        StringAssert.Contains(sql, "request.status = 'Open'");
    }

    [TestMethod]
    public void RollbackRemovesTriggerWithoutReopeningCompletedRequests()
    {
        var migration = new AutoCompleteSellerModificationOnRevisionAdvance();
        var sql = string.Join("\n", migration.DownOperations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql));

        StringAssert.Contains(sql, "DROP TRIGGER IF EXISTS trg_complete_seller_modification_on_revision");
        StringAssert.Contains(sql, "DROP FUNCTION IF EXISTS pricing.complete_seller_modification_on_revision()");
        Assert.IsFalse(sql.Contains("UPDATE pricing.\"RateRequests\"", StringComparison.OrdinalIgnoreCase));
    }
}
