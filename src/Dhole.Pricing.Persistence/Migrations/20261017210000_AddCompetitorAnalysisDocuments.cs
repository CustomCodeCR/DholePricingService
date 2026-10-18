using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261017210000_AddCompetitorAnalysisDocuments")]
public partial class AddCompetitorAnalysisDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."CompetitorAnalysisDocuments" (
                "Id" uuid NOT NULL,
                "StorageId" uuid NOT NULL,
                "OriginalFileName" character varying(255) NOT NULL,
                "SizeInBytes" bigint NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_CompetitorAnalysisDocuments" PRIMARY KEY ("Id"),
                CONSTRAINT "CK_CompetitorAnalysisDocuments_SizeInBytes" CHECK ("SizeInBytes" > 0)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_CompetitorAnalysisDocuments_StorageId"
                ON pricing."CompetitorAnalysisDocuments" ("StorageId");
            CREATE INDEX IF NOT EXISTS "IX_CompetitorAnalysisDocuments_CreatedAtUtc"
                ON pricing."CompetitorAnalysisDocuments" ("CreatedAtUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TABLE IF EXISTS pricing."CompetitorAnalysisDocuments";""");
    }
}
