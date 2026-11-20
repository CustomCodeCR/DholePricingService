using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// Allows a one-day in-app notification to be queued independently from the
/// existing one-day email marker, including quotations already emailed.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261119193000_AddOneDaySystemRateExpiryMarker")]
public sealed class AddOneDaySystemRateExpiryMarker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."RateExpiryReminderQueue"
                DROP CONSTRAINT "CK_RateExpiryReminderQueue_Milestone";
            ALTER TABLE pricing."RateExpiryReminderQueue"
                ALTER COLUMN "Milestone" TYPE varchar(16);
            ALTER TABLE pricing."RateExpiryReminderQueue"
                ADD CONSTRAINT "CK_RateExpiryReminderQueue_Milestone"
                    CHECK ("Milestone" IN ('5d', '3d', '1d', '6h', '1d-system'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM pricing."RateExpiryReminderQueue" WHERE "Milestone" = '1d-system';
            ALTER TABLE pricing."RateExpiryReminderQueue"
                DROP CONSTRAINT "CK_RateExpiryReminderQueue_Milestone";
            ALTER TABLE pricing."RateExpiryReminderQueue"
                ALTER COLUMN "Milestone" TYPE varchar(4);
            ALTER TABLE pricing."RateExpiryReminderQueue"
                ADD CONSTRAINT "CK_RateExpiryReminderQueue_Milestone"
                    CHECK ("Milestone" IN ('5d', '3d', '1d', '6h'));
            """);
    }
}