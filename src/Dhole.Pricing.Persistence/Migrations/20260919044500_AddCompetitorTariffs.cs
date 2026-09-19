using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260919044500_AddCompetitorTariffs")]
public partial class AddCompetitorTariffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."CompetitorTariffs" (
                "Id" uuid NOT NULL,
                "PolIds" uuid[] NOT NULL,
                "PoeIds" uuid[] NOT NULL,
                "PodIds" uuid[] NOT NULL,
                "CarrierIds" uuid[] NOT NULL,
                "ValidFrom" timestamp with time zone NOT NULL,
                "ValidTo" timestamp with time zone NOT NULL,
                "ShipmentMode" character varying(20) NOT NULL,
                "StorageId" uuid NOT NULL,
                CONSTRAINT "PK_CompetitorTariffs" PRIMARY KEY ("Id"),
                CONSTRAINT "CK_CompetitorTariffs_Validity" CHECK ("ValidTo" >= "ValidFrom"),
                CONSTRAINT "CK_CompetitorTariffs_PolIds" CHECK (cardinality("PolIds") > 0),
                CONSTRAINT "CK_CompetitorTariffs_PoeIds" CHECK (cardinality("PoeIds") > 0),
                CONSTRAINT "CK_CompetitorTariffs_PodIds" CHECK (cardinality("PodIds") > 0),
                CONSTRAINT "CK_CompetitorTariffs_CarrierIds" CHECK (cardinality("CarrierIds") > 0)
            );

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_ShipmentMode"
                ON pricing."CompetitorTariffs" ("ShipmentMode");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_ValidTo"
                ON pricing."CompetitorTariffs" ("ValidTo");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_PolIds_Gin"
                ON pricing."CompetitorTariffs" USING GIN ("PolIds");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_PoeIds_Gin"
                ON pricing."CompetitorTariffs" USING GIN ("PoeIds");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_PodIds_Gin"
                ON pricing."CompetitorTariffs" USING GIN ("PodIds");

            CREATE INDEX IF NOT EXISTS "IX_CompetitorTariffs_CarrierIds_Gin"
                ON pricing."CompetitorTariffs" USING GIN ("CarrierIds");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS pricing."CompetitorTariffs";
            """);
    }
}
