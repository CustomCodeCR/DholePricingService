using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260929181500_AddLtlTariffChargeItems")]
public sealed class AddLtlTariffChargeItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                ADD COLUMN IF NOT EXISTS ltl_charge_items text NULL;

            UPDATE pricing."FtlTariffs"
            SET ltl_charge_items = jsonb_build_array(
                jsonb_build_object(
                    'Key', 'dua',
                    'Name', 'DUA',
                    'CostDetailType', 'CustomsCharge',
                    'ChargeBasis', 'PerDocument',
                    'Section', 'origin_charges',
                    'CostAmount', COALESCE(dua_cost, 50),
                    'SaleAmount', 60,
                    'IsFlat', TRUE
                ),
                jsonb_build_object(
                    'Key', 'duca-t',
                    'Name', 'DUCA-T',
                    'CostDetailType', 'Documentation',
                    'ChargeBasis', 'PerDocument',
                    'Section', 'international_freight',
                    'CostAmount', COALESCE(duca_t_cost, 30),
                    'SaleAmount', CASE WHEN lower(commercial_profile) = 'nvocc' THEN 30 ELSE 35 END,
                    'IsFlat', TRUE
                ),
                jsonb_build_object(
                    'Key', 'stuffing',
                    'Name', 'Stuffing',
                    'CostDetailType', 'OriginCharge',
                    'ChargeBasis', 'PerChargeableCbm',
                    'Section', 'origin_charges',
                    'CostAmount', COALESCE(stuffing_cost_per_cbm, 550.0 / 60.0),
                    'SaleAmount', COALESCE(stuffing_sale_per_cbm, 10),
                    'IsFlat', TRUE
                ),
                jsonb_build_object(
                    'Key', 'carta-porte',
                    'Name', 'Carta Porte',
                    'CostDetailType', 'Documentation',
                    'ChargeBasis', 'PerDocument',
                    'Section', 'international_freight',
                    'CostAmount', 0,
                    'SaleAmount', CASE WHEN lower(commercial_profile) = 'nvocc' THEN 35 ELSE 45 END,
                    'IsFlat', TRUE
                ),
                jsonb_build_object(
                    'Key', 'manejos',
                    'Name', 'Manejos',
                    'CostDetailType', 'AgentCharge',
                    'ChargeBasis', 'PerShipment',
                    'Section', 'origin_charges',
                    'CostAmount', 0,
                    'SaleAmount', CASE WHEN lower(commercial_profile) = 'nvocc' THEN 25 ELSE 45 END,
                    'IsFlat', TRUE
                ),
                jsonb_build_object('Key', 'seguro', 'Name', 'Seguro', 'CostDetailType', 'Insurance', 'ChargeBasis', 'PerShipment', 'Section', 'origin_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'recolecta', 'Name', 'Recolecta', 'CostDetailType', 'OriginCharge', 'ChargeBasis', 'PerShipment', 'Section', 'pickup_origin', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'reembarque', 'Name', 'Reembarque', 'CostDetailType', 'Other', 'ChargeBasis', 'PerShipment', 'Section', 'origin_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'inspeccion', 'Name', 'Inspección', 'CostDetailType', 'CustomsCharge', 'ChargeBasis', 'PerShipment', 'Section', 'origin_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'tramite-aduanas-destino', 'Name', 'Trámite Aduanas Destino', 'CostDetailType', 'CustomsCharge', 'ChargeBasis', 'PerShipment', 'Section', 'destination_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'entrega-destino', 'Name', 'Entrega en Destino', 'CostDetailType', 'InlandTransport', 'ChargeBasis', 'PerShipment', 'Section', 'delivery_destination', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'otros', 'Name', 'Otros', 'CostDetailType', 'Other', 'ChargeBasis', 'PerShipment', 'Section', 'destination_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'duca-f', 'Name', 'DUCA-F', 'CostDetailType', 'Documentation', 'ChargeBasis', 'PerDocument', 'Section', 'international_freight', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'impuesto-exportacion', 'Name', 'Impuesto Exportación', 'CostDetailType', 'CustomsCharge', 'ChargeBasis', 'PerShipment', 'Section', 'origin_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE),
                jsonb_build_object('Key', 'recepcion-destino', 'Name', 'Recepción en Destino', 'CostDetailType', 'DestinationCharge', 'ChargeBasis', 'PerShipment', 'Section', 'destination_charges', 'CostAmount', NULL, 'SaleAmount', NULL, 'IsFlat', FALSE)
            )::text,
            updated_at_utc = NOW()
            WHERE lower(shipment_mode) = 'ltl'
              AND NULLIF(trim(COALESCE(ltl_charge_items, '')), '') IS NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."FtlTariffs"
                DROP COLUMN IF EXISTS ltl_charge_items;
            """
        );
    }
}
