using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260910123000_AddCostMultiPartySelections")]
public sealed class AddCostMultiPartySelections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."CostPartySelections"
            (
                cost_id uuid NOT NULL,
                party_type character varying(8) NOT NULL,
                party_id uuid NOT NULL,
                CONSTRAINT "PK_CostPartySelections" PRIMARY KEY (cost_id, party_type, party_id),
                CONSTRAINT "FK_CostPartySelections_Costs_cost_id"
                    FOREIGN KEY (cost_id)
                    REFERENCES pricing."Costs" (id)
                    ON DELETE CASCADE,
                CONSTRAINT "CK_CostPartySelections_party_type"
                    CHECK (party_type IN ('Carrier', 'Agent'))
            );

            CREATE INDEX IF NOT EXISTS "IX_CostPartySelections_party_type_party_id"
                ON pricing."CostPartySelections" (party_type, party_id);

            INSERT INTO pricing."CostPartySelections" (cost_id, party_type, party_id)
            SELECT id, 'Carrier', carrier_id
            FROM pricing."Costs"
            WHERE carrier_id IS NOT NULL
            ON CONFLICT DO NOTHING;

            INSERT INTO pricing."CostPartySelections" (cost_id, party_type, party_id)
            SELECT id, 'Agent', agent_id
            FROM pricing."Costs"
            WHERE agent_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."CostPartySelections";
            """
        );
    }
}