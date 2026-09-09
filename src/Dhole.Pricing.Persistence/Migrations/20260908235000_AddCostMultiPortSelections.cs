using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260908235000_AddCostMultiPortSelections")]
public sealed class AddCostMultiPortSelections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."CostRoutePortSelections"
            (
                cost_id uuid NOT NULL,
                role character varying(8) NOT NULL,
                port_id uuid NOT NULL,
                CONSTRAINT "PK_CostRoutePortSelections" PRIMARY KEY (cost_id, role, port_id),
                CONSTRAINT "FK_CostRoutePortSelections_Costs_cost_id"
                    FOREIGN KEY (cost_id)
                    REFERENCES pricing."Costs" (id)
                    ON DELETE CASCADE,
                CONSTRAINT "CK_CostRoutePortSelections_role"
                    CHECK (role IN ('Pol', 'Poe', 'Pod'))
            );

            CREATE INDEX IF NOT EXISTS "IX_CostRoutePortSelections_role_port_id"
                ON pricing."CostRoutePortSelections" (role, port_id);

            INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
            SELECT id, 'Pol', pol_id
            FROM pricing."Costs"
            WHERE pol_id IS NOT NULL
            ON CONFLICT DO NOTHING;

            INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
            SELECT id, 'Poe', poe_id
            FROM pricing."Costs"
            WHERE poe_id IS NOT NULL
            ON CONFLICT DO NOTHING;

            INSERT INTO pricing."CostRoutePortSelections" (cost_id, role, port_id)
            SELECT id, 'Pod', pod_id
            FROM pricing."Costs"
            WHERE pod_id IS NOT NULL
            ON CONFLICT DO NOTHING;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."CostRoutePortSelections";
            """
        );
    }
}
