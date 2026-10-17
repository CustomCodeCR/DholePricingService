using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

/// <summary>
/// One-time recovery of competitor PDF imports that hit the old 120-second
/// gRPC deadline before DataExtraction stopped duplicating Storage/Config I/O.
/// Only files still present in the durable queue, without any observations,
/// qualify. Processed/reviewable imports are never touched.
/// </summary>
[DbContext(typeof(ServiceDbContext))]
[Migration("20261017133000_RequeueTimedOutCompetitorPdfImports")]
public partial class RequeueTimedOutCompetitorPdfImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE _competitor_import_timeout_recovery
            ON COMMIT DROP AS
            SELECT t."Id"
            FROM pricing."CompetitorTariffs" t
            INNER JOIN pricing."CompetitorTariffImportPayloads" p
                ON p."CompetitorTariffId" = t."Id"
            WHERE t."ImportStatus" = 'Failed'
              AND t."ImportErrorCode" = 'Grpc.DeadlineExceeded'
              AND t."ObservationCount" = 0
              AND octet_length(p."FileContent") > 0
              AND t."ValidTo" >= CURRENT_DATE
              AND NOT EXISTS (
                  SELECT 1
                  FROM pricing."CompetitorRateObservations" o
                  WHERE o."competitor_tariff_id" = t."Id"
              );

            UPDATE pricing."CompetitorTariffImportPayloads" p
               SET "AttemptCount" = 0,
                   "LeaseUntilUtc" = NULL,
                   "NextAttemptAtUtc" = now()
              FROM _competitor_import_timeout_recovery r
             WHERE p."CompetitorTariffId" = r."Id";

            UPDATE pricing."CompetitorTariffs" t
               SET "ImportStatus" = 'Processing',
                   "ImportedAtUtc" = now(),
                   "ObservationCount" = 0,
                   "ReviewCount" = 0,
                   "ExtractionExecutionId" = NULL,
                   "ImportErrorCode" = NULL,
                   "ImportErrorMessage" = NULL
              FROM _competitor_import_timeout_recovery r
             WHERE t."Id" = r."Id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Extraction recovery is an operational action, not a reversible schema change.
    }
}
