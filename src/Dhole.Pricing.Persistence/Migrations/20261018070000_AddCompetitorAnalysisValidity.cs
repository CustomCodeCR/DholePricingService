using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261018070000_AddCompetitorAnalysisValidity")]
public partial class AddCompetitorAnalysisValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."CompetitorAnalysisDocuments"
                ADD COLUMN IF NOT EXISTS "ValidFromUtc" timestamp with time zone NULL;
            ALTER TABLE pricing."CompetitorAnalysisDocuments"
                ADD COLUMN IF NOT EXISTS "ValidToUtc" timestamp with time zone NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE pricing."CompetitorAnalysisDocuments" DROP COLUMN IF EXISTS "ValidFromUtc";
            ALTER TABLE pricing."CompetitorAnalysisDocuments" DROP COLUMN IF EXISTS "ValidToUtc";
            """);
    }
}
