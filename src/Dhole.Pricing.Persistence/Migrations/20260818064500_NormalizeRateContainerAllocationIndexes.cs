using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class NormalizeRateContainerAllocationIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF to_regclass('pricing."RateContainerAllocations"') IS NOT NULL THEN
                    IF to_regclass('pricing."IX_RateContainerAllocations_rate_header_id"') IS NOT NULL
                       AND to_regclass('pricing.i_x_rate_container_allocations_rate_header_id') IS NULL THEN
                        ALTER INDEX pricing."IX_RateContainerAllocations_rate_header_id"
                            RENAME TO i_x_rate_container_allocations_rate_header_id;
                    END IF;

                    IF to_regclass('pricing.i_x_rate_container_allocations_rate_header_id') IS NULL THEN
                        CREATE INDEX i_x_rate_container_allocations_rate_header_id
                            ON pricing."RateContainerAllocations" (rate_header_id);
                    END IF;

                    IF to_regclass('pricing."IX_RateContainerAllocations_container_type_id"') IS NOT NULL
                       AND to_regclass('pricing.i_x_rate_container_allocations_container_type_id') IS NULL THEN
                        ALTER INDEX pricing."IX_RateContainerAllocations_container_type_id"
                            RENAME TO i_x_rate_container_allocations_container_type_id;
                    END IF;

                    IF to_regclass('pricing.i_x_rate_container_allocations_container_type_id') IS NULL THEN
                        CREATE INDEX i_x_rate_container_allocations_container_type_id
                            ON pricing."RateContainerAllocations" (container_type_id);
                    END IF;
                END IF;
            END $$;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op intentionally: this migration only normalizes index names/availability.
        // The model before and after this repair migration is identical.
    }
}
