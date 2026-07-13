using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260713230000_AllowImportedRateReuse")]
public sealed class AllowImportedRateReuse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_rate_headers_source_import_fcl_rate_id_unique",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.CreateIndex(
            name: "ix_rate_headers_source_import_fcl_rate_id",
            schema: "pricing",
            table: "RateHeaders",
            column: "source_import_fcl_rate_id"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_rate_headers_source_import_fcl_rate_id",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.CreateIndex(
            name: "ix_rate_headers_source_import_fcl_rate_id_unique",
            schema: "pricing",
            table: "RateHeaders",
            column: "source_import_fcl_rate_id",
            unique: true,
            filter: "source_import_fcl_rate_id IS NOT NULL AND is_deleted = false"
        );
    }
}
