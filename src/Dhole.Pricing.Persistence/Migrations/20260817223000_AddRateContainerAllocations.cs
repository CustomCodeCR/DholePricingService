using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddRateContainerAllocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RateContainerAllocations",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rate_header_id = table.Column<Guid>(type: "uuid", nullable: false),
                container_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                container_type_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                container_type_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                quantity = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("p_k_rate_container_allocations", x => x.id);
                table.ForeignKey(
                    name: "f_k_rate_container_allocations__rate_headers_rate_header_id",
                    column: x => x.rate_header_id,
                    principalSchema: "pricing",
                    principalTable: "RateHeaders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Backfill every existing rate so legacy rows immediately have one allocation.
        migrationBuilder.Sql(
            """
            INSERT INTO pricing."RateContainerAllocations"
                (id, rate_header_id, container_type_id, container_type_name, container_type_code, quantity)
            SELECT id, id, container_type_id, container_type_name, container_type_code,
                   CASE WHEN container_quantity > 0 THEN container_quantity ELSE 1 END
            FROM pricing."RateHeaders";
            """);

        migrationBuilder.CreateIndex(
            name: "i_x_rate_container_allocations_container_type_id",
            schema: "pricing",
            table: "RateContainerAllocations",
            column: "container_type_id");

        migrationBuilder.CreateIndex(
            name: "i_x_rate_container_allocations_rate_header_id",
            schema: "pricing",
            table: "RateContainerAllocations",
            column: "rate_header_id");

        migrationBuilder.CreateIndex(
            name: "ux_rate_container_allocations_rate_container",
            schema: "pricing",
            table: "RateContainerAllocations",
            columns: new[] { "rate_header_id", "container_type_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RateContainerAllocations",
            schema: "pricing");
    }
}
