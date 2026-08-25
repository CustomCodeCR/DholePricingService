using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260825235000_AddCommercialRouteTermRules")]
public sealed class AddCommercialRouteTermRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateTermBlocks"
                ADD COLUMN IF NOT EXISTS route_key character varying(120) NULL;

            CREATE INDEX IF NOT EXISTS "IX_RateTermBlocks_route_key"
                ON pricing."RateTermBlocks" (route_key);

            INSERT INTO pricing."RateTermBlocks"
                (id,name,rate_type,shipment_mode,poe_id,poe_name,poe_code,incoterm_id,incoterm_name,incoterm_code,sort_order,is_active,created_at_utc,transport_modality,direction,route_key)
            VALUES
                ('d3000000-0000-4000-8000-000000000001','CRC · FCL importación · Moín',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,45,TRUE,NOW(),'Maritime','Importación','moin'),
                ('d3000000-0000-4000-8000-000000000002','CRC · FCL importación · Caldera',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,45,TRUE,NOW(),'Maritime','Importación','caldera'),
                ('d3000000-0000-4000-8000-000000000003','CRC · FCL exportación EXW · Moín',NULL,'Fcl',NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000001','EXW','EXW',45,TRUE,NOW(),'Maritime','Exportación','moin'),
                ('d3000000-0000-4000-8000-000000000004','CRC · FCL exportación EXW · Caldera',NULL,'Fcl',NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000001','EXW','EXW',45,TRUE,NOW(),'Maritime','Exportación','caldera'),
                ('d3000000-0000-4000-8000-000000000005','CRC · FCL exportación FOB · Moín',NULL,'Fcl',NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000004','FOB','FOB',45,TRUE,NOW(),'Maritime','Exportación','moin'),
                ('d3000000-0000-4000-8000-000000000006','CRC · FCL exportación FOB · Caldera',NULL,'Fcl',NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000004','FOB','FOB',45,TRUE,NOW(),'Maritime','Exportación','caldera'),
                ('d3000000-0000-4000-8000-000000000007','CRC · EXW exportación · Impuesto',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000001','EXW','EXW',25,TRUE,NOW(),NULL,'Exportación',NULL)
            ON CONFLICT (id) DO UPDATE SET
                name=EXCLUDED.name, shipment_mode=EXCLUDED.shipment_mode,
                incoterm_id=EXCLUDED.incoterm_id, incoterm_name=EXCLUDED.incoterm_name,
                incoterm_code=EXCLUDED.incoterm_code, sort_order=EXCLUDED.sort_order,
                is_active=TRUE, transport_modality=EXCLUDED.transport_modality,
                direction=EXCLUDED.direction, route_key=EXCLUDED.route_key,
                updated_at_utc=NOW();

            WITH desired(block_id,item_id,category,sort_order) AS (VALUES
                ('d3000000-0000-4000-8000-000000000001'::uuid,'e7b8f2cc-ffe7-45d1-8a49-64dd3a549f4d'::uuid,'Includes',10),
                ('d3000000-0000-4000-8000-000000000002'::uuid,'67bb587e-36f4-4c55-9d76-8e6b92961b2b'::uuid,'Includes',10),
                ('d3000000-0000-4000-8000-000000000003'::uuid,'630f7dd2-7584-4971-9e9e-bc5141127e09'::uuid,'Includes',10),
                ('d3000000-0000-4000-8000-000000000004'::uuid,'15181b43-8489-4619-8d20-026a5d24b4c8'::uuid,'Includes',10),
                ('d3000000-0000-4000-8000-000000000005'::uuid,'630f7dd2-7584-4971-9e9e-bc5141127e09'::uuid,'Excludes',10),
                ('d3000000-0000-4000-8000-000000000006'::uuid,'15181b43-8489-4619-8d20-026a5d24b4c8'::uuid,'Excludes',10),
                ('d3000000-0000-4000-8000-000000000007'::uuid,'e3a13c5c-5501-4681-9b20-d915c0ff2b59'::uuid,'Includes',10)
            )
            INSERT INTO pricing."RateTermBlockItems" (block_id,rate_term_item_id,category,sort_order)
            SELECT d.block_id,d.item_id,d.category,d.sort_order
            FROM desired d
            JOIN pricing."RateTermItems" t ON t.id=d.item_id
            ON CONFLICT (block_id,rate_term_item_id) DO UPDATE
                SET category=EXCLUDED.category, sort_order=EXCLUDED.sort_order;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM pricing."RateTermBlocks" WHERE id::text LIKE 'd3000000-%';
            DROP INDEX IF EXISTS pricing."IX_RateTermBlocks_route_key";
            ALTER TABLE pricing."RateTermBlocks" DROP COLUMN IF EXISTS route_key;
            """
        );
    }
}
