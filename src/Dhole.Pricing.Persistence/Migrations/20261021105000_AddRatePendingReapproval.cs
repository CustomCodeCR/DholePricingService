using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261021105000_AddRatePendingReapproval")]
public sealed class AddRatePendingReapproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "pending_reapproval", schema: "pricing", table: "RateHeaders",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(
            name: "reapproval_reason", schema: "pricing", table: "RateHeaders",
            type: "text", nullable: true);

        // Recuperar revisiones aceptadas ya editadas antes de este despliegue.
        // Sus motivos anteriores permanecen en auditoría, no se inventan motivos.
        migrationBuilder.Sql("""
            UPDATE pricing."RateHeaders" AS rate
            SET pending_reapproval = TRUE
            WHERE rate.status IN ('PendingApproval', 'ApprovedByManagement', 'RejectedByManagement',
                                  'Open', 'Sent', 'RequestedByClient')
              AND EXISTS (
                  SELECT 1 FROM pricing."RateRevisions" AS revision
                  WHERE revision."RateHeaderId" = rate.id
                    AND revision."Status" = 'AcceptedByClient'
                    AND revision."RevisionNumber" < rate.revision_number
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "reapproval_reason", schema: "pricing", table: "RateHeaders");
        migrationBuilder.DropColumn(name: "pending_reapproval", schema: "pricing", table: "RateHeaders");
    }
}
