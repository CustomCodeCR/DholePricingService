using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260903235900_NormalizeLclEquipmentMetadata")]
public sealed class NormalizeLclEquipmentMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET container_type_name = 'LCL',
                container_type_code = 'LCL',
                container_quantity = 0,
                free_days = 0
            WHERE LOWER(shipment_mode) = 'lcl';

            DELETE FROM pricing."RateContainerAllocations" allocations
            USING pricing."RateHeaders" headers
            WHERE allocations.rate_header_id = headers.id
              AND LOWER(headers.shipment_mode) = 'lcl';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data normalization is intentionally non-reversible.
    }
}
