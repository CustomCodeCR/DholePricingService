using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistRateDetailDestinationTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "apply_destination_tax",
                schema: "pricing",
                table: "RateDetails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "destination_tax_rate",
                schema: "pricing",
                table: "RateDetails",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Backfill legacy IVA state that older Web versions stored only in Notes.
            migrationBuilder.Sql(
                """
                UPDATE pricing."RateDetails"
                SET apply_destination_tax = TRUE,
                    destination_tax_rate = COALESCE(
                        NULLIF(substring(notes from 'IVA\s+([0-9]+(?:\.[0-9]+)?)%'), '')::numeric,
                        13
                    )
                WHERE notes ~* 'IVA\s+[0-9]+(?:\.[0-9]+)?%';
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "apply_destination_tax",
                schema: "pricing",
                table: "RateDetails");

            migrationBuilder.DropColumn(
                name: "destination_tax_rate",
                schema: "pricing",
                table: "RateDetails");
        }
    }
}
