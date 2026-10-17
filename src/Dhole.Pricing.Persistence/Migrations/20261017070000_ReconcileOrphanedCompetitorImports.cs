using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261017070000_ReconcileOrphanedCompetitorImports")]
public partial class ReconcileOrphanedCompetitorImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Old production kept PDFs only inside an in-memory channel. After a
        // restart those records stay Processing permanently. Only reconcile
        // jobs with NO durable payload, and never touch recent in-flight work.
        migrationBuilder.Sql("""
            UPDATE pricing."CompetitorTariffs" t
            SET "ImportStatus" = 'Failed',
                "ReviewCount" = GREATEST(t."ReviewCount", 1),
                "ImportErrorCode" = 'CompetitorTariff.FileReuploadRequired',
                "ImportErrorMessage" =
                  'La importación anterior no conserva una cola persistente. Adjunte otra vez el PDF desde Reintentar.'
            WHERE t."ImportStatus" = 'Processing'
              AND t."ImportedAtUtc" < now() - interval '10 minutes'
              AND NOT EXISTS (
                  SELECT 1 FROM pricing."CompetitorTariffImportPayloads" p
                  WHERE p."CompetitorTariffId" = t."Id"
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // State reconciliation is not reversibly a schema migration.
    }
}
