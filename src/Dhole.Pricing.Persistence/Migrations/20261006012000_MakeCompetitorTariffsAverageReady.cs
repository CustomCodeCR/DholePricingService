using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261006012000_MakeCompetitorTariffsAverageReady")]
public partial class MakeCompetitorTariffsAverageReady : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."CompetitorTariffs"
                ADD COLUMN IF NOT EXISTS "CompetitorCompanyName" character varying(250) NOT NULL DEFAULT 'Competencia',
                ADD COLUMN IF NOT EXISTS "IncotermId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "OriginalFileName" character varying(500) NULL,
                ADD COLUMN IF NOT EXISTS "ExtractionExecutionId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "ObservationCount" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "ReviewCount" integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS "ImportStatus" character varying(40) NOT NULL DEFAULT 'Legacy',
                ADD COLUMN IF NOT EXISTS "ImportedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW();

            ALTER TABLE pricing."CompetitorTariffs"
                DROP CONSTRAINT IF EXISTS "CK_CompetitorTariffs_PolIds",
                DROP CONSTRAINT IF EXISTS "CK_CompetitorTariffs_PoeIds",
                DROP CONSTRAINT IF EXISTS "CK_CompetitorTariffs_PodIds",
                DROP CONSTRAINT IF EXISTS "CK_CompetitorTariffs_CarrierIds";

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_CompetitorCompanyName"
                ON pricing."CompetitorTariffs" ("CompetitorCompanyName");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_ImportStatus"
                ON pricing."CompetitorTariffs" ("ImportStatus");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS pricing."IX_CompetitorTariffs_ImportStatus";
            DROP INDEX IF EXISTS pricing."IX_CompetitorTariffs_CompetitorCompanyName";

            ALTER TABLE pricing."CompetitorTariffs"
                DROP COLUMN IF EXISTS "ImportedAtUtc",
                DROP COLUMN IF EXISTS "ImportStatus",
                DROP COLUMN IF EXISTS "ReviewCount",
                DROP COLUMN IF EXISTS "ObservationCount",
                DROP COLUMN IF EXISTS "ExtractionExecutionId",
                DROP COLUMN IF EXISTS "OriginalFileName",
                DROP COLUMN IF EXISTS "IncotermId",
                DROP COLUMN IF EXISTS "CompetitorCompanyName";
            """);
    }
}
