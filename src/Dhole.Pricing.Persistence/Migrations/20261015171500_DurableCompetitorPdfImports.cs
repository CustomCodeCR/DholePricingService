using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261015171500_DurableCompetitorPdfImports")]
public partial class DurableCompetitorPdfImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."CompetitorTariffs"
                ADD COLUMN IF NOT EXISTS "ImportErrorCode" character varying(120) NULL,
                ADD COLUMN IF NOT EXISTS "ImportErrorMessage" character varying(2000) NULL;

            CREATE TABLE IF NOT EXISTS pricing."CompetitorTariffImportPayloads"
            (
                "CompetitorTariffId" uuid NOT NULL PRIMARY KEY
                    REFERENCES pricing."CompetitorTariffs"("Id") ON DELETE CASCADE,
                "FileContent" bytea NOT NULL,
                "FileHash" character varying(64) NOT NULL,
                "ContentType" character varying(150) NULL,
                "FallbackFrom" timestamp with time zone NULL,
                "FallbackTo" timestamp with time zone NULL,
                "RequestedBy" uuid NULL,
                "RequestedByName" character varying(250) NULL,
                "AttemptCount" integer NOT NULL DEFAULT 0,
                "NextAttemptAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                "LeaseUntilUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffImportPayloads_NextAttemptAtUtc_LeaseUntilUtc"
                ON pricing."CompetitorTariffImportPayloads" ("NextAttemptAtUtc", "LeaseUntilUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS pricing."CompetitorTariffImportPayloads";
            ALTER TABLE pricing."CompetitorTariffs"
                DROP COLUMN IF EXISTS "ImportErrorMessage",
                DROP COLUMN IF EXISTS "ImportErrorCode";
            """);
    }
}
