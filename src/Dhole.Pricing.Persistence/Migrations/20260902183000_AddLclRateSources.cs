using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddLclRateSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."LclRateSources" (
                "Id" uuid NOT NULL PRIMARY KEY,
                "SourceType" character varying(16) NOT NULL,
                "BookingNumber" character varying(100) NULL,
                "Etd" timestamp with time zone NULL,
                "ProviderId" uuid NULL,
                "ProviderName" character varying(200) NULL,
                "ProviderCode" character varying(100) NULL,
                "CarrierId" uuid NOT NULL,
                "CarrierName" character varying(200) NOT NULL,
                "CarrierCode" character varying(100) NOT NULL,
                "PolId" uuid NOT NULL,
                "PolName" character varying(200) NOT NULL,
                "PolCode" character varying(100) NOT NULL,
                "PoeId" uuid NOT NULL,
                "PoeName" character varying(200) NOT NULL,
                "PoeCode" character varying(100) NOT NULL,
                "ContainerTypeId" uuid NULL,
                "ContainerTypeName" character varying(200) NULL,
                "ContainerTypeCode" character varying(100) NULL,
                "MaxCbm" numeric(12,3) NULL,
                "OceanFreightAmount" numeric(18,2) NULL,
                "DestinationCostTotal" numeric(18,2) NULL,
                "OceanFreightPerCbm" numeric(18,4) NULL,
                "DestinationCostPerCbm" numeric(18,4) NULL,
                "BaseRatePerCbm" numeric(18,4) NOT NULL,
                "CurrencyId" uuid NOT NULL,
                "CurrencyName" character varying(100) NOT NULL,
                "CurrencyCode" character varying(20) NOT NULL,
                "ApprovalStatus" character varying(32) NOT NULL,
                "ValidFrom" timestamp with time zone NULL,
                "ValidTo" timestamp with time zone NULL,
                "DefaultLandFreightAmount" numeric(18,2) NOT NULL DEFAULT 2140,
                "DefaultBunkerAmount" numeric(18,2) NOT NULL DEFAULT 280,
                "TruckCapacityCbm" numeric(12,3) NOT NULL DEFAULT 95,
                "Notes" text NULL,
                "ApprovedAtUtc" timestamp with time zone NULL,
                "ApprovedBy" character varying(200) NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS "IX_LclRateSources_Route"
                ON pricing."LclRateSources" ("PolId", "PoeId", "SourceType", "ApprovalStatus");

            CREATE INDEX IF NOT EXISTS "IX_LclRateSources_Etd"
                ON pricing."LclRateSources" ("Etd");

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_LclRateSources_OwnBooking"
                ON pricing."LclRateSources" ("BookingNumber")
                WHERE "SourceType" = 'Own' AND "BookingNumber" IS NOT NULL AND "IsActive" = TRUE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS pricing.\"LclRateSources\";");
    }
}
