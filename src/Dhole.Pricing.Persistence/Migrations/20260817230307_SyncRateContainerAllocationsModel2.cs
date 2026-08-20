using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncRateContainerAllocationsModel2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddRateContainerAllocations already creates the index with the normalized
            // i_x_ name. Some databases may still contain the convention-based IX_ name,
            // so normalize it only when that legacy index actually exists.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('pricing."IX_RateContainerAllocations_rate_header_id"') IS NOT NULL
                       AND to_regclass('pricing.i_x_rate_container_allocations_rate_header_id') IS NULL THEN
                        ALTER INDEX pricing."IX_RateContainerAllocations_rate_header_id"
                            RENAME TO i_x_rate_container_allocations_rate_header_id;
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. The migration does not introduce a model change; it only repairs
            // a legacy index name when necessary. AddRateContainerAllocations already
            // uses the normalized i_x_ name.
        }
    }
}
