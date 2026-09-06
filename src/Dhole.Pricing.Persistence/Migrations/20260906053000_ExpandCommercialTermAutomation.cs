using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260906053000_ExpandCommercialTermAutomation")]
public sealed class ExpandCommercialTermAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Catálogo comercial basado en el Manual de Items Venta de Pricing CRC.
            -- Se inserta por texto para reutilizar ítems ya existentes y evitar duplicados.
            WITH desired(id, text, sort_order) AS (VALUES
                ('e6100000-0000-4000-8000-000000000001'::uuid,'Flete internacional',10),
                ('e6100000-0000-4000-8000-000000000002'::uuid,'Manejos',20),
                ('e6100000-0000-4000-8000-000000000003'::uuid,'HBL',30),
                ('e6100000-0000-4000-8000-000000000004'::uuid,'HAWB',40),
                ('e6100000-0000-4000-8000-000000000005'::uuid,'Cargos en destino de la línea naviera',50),
                ('e6100000-0000-4000-8000-000000000006'::uuid,'Cargos en destino de la aerolínea',60),
                ('e6100000-0000-4000-8000-000000000007'::uuid,'Recolección',70),
                ('e6100000-0000-4000-8000-000000000008'::uuid,'Trámite de exportación',80),
                ('e6100000-0000-4000-8000-000000000009'::uuid,'Cargos en origen',90),
                ('e6100000-0000-4000-8000-000000000010'::uuid,'Impuesto de exportación marítimo USD 3.00',100),
                ('e6100000-0000-4000-8000-000000000011'::uuid,'Impuesto de exportación terrestre USD 28.00',110),
                ('e6100000-0000-4000-8000-000000000012'::uuid,'Forwarding USD 50.00',120),
                ('e6100000-0000-4000-8000-000000000013'::uuid,'Bunker según tarifario Miami',130),
                ('e6100000-0000-4000-8000-000000000014'::uuid,'THC/D según tarifario Miami',140),
                ('e6100000-0000-4000-8000-000000000015'::uuid,'SED USD 25 por factura de proveedor mayor a USD 2,500.00',150),
                ('e6100000-0000-4000-8000-000000000016'::uuid,'Carga peligrosa USD 250.00',160),
                ('e6100000-0000-4000-8000-000000000017'::uuid,'Carta Porte USD 40.00',170),
                ('e6100000-0000-4000-8000-000000000018'::uuid,'Manifiesto de Carga USD 40.00',180),
                ('e6100000-0000-4000-8000-000000000019'::uuid,'DUCA-T USD 40.00',190),
                ('e6100000-0000-4000-8000-000000000020'::uuid,'DUCA-F USD 40.00 si aplica',200),
                ('e6100000-0000-4000-8000-000000000021'::uuid,'Inland Puerto Caldera a SJO',210),
                ('e6100000-0000-4000-8000-000000000022'::uuid,'Inland Puerto Moín a SJO',220),
                ('e6100000-0000-4000-8000-000000000023'::uuid,'Muellaje',230),
                ('e6100000-0000-4000-8000-000000000024'::uuid,'Anticipado o Redestino según puerto y naviera',240),
                ('e6100000-0000-4000-8000-000000000025'::uuid,'Marchamo',250),
                ('e6100000-0000-4000-8000-000000000026'::uuid,'Retiro vacío',260),
                ('e6100000-0000-4000-8000-000000000027'::uuid,'Sobrepeso / Patio',270),
                ('e6100000-0000-4000-8000-000000000028'::uuid,'Demoras de chasis',280),
                ('e6100000-0000-4000-8000-000000000029'::uuid,'Demoras de contenedor',290),
                ('e6100000-0000-4000-8000-000000000030'::uuid,'Carrusel',300),
                ('e6100000-0000-4000-8000-000000000031'::uuid,'Carta de transbordo',310),
                ('e6100000-0000-4000-8000-000000000032'::uuid,'IVA de los cargos en destino',320),
                ('e6100000-0000-4000-8000-000000000033'::uuid,'Inspecciones, revisiones y escáner',330),
                ('e6100000-0000-4000-8000-000000000034'::uuid,'Mensajería',340),
                ('e6100000-0000-4000-8000-000000000035'::uuid,'Cambios sin previo aviso',350),
                ('e6100000-0000-4000-8000-000000000036'::uuid,'Retiro de guía aérea en destino USD 65 + IVA',360),
                ('e6100000-0000-4000-8000-000000000037'::uuid,'Certificado de reexportación USD 125.00',370),
                ('e6100000-0000-4000-8000-000000000038'::uuid,'No sobrepeso',380),
                ('e6100000-0000-4000-8000-000000000039'::uuid,'HUB de transbordo en Panamá',390),
                ('e6100000-0000-4000-8000-000000000040'::uuid,'Inland Panamá a Costa Rica',400),
                ('e6100000-0000-4000-8000-000000000041'::uuid,'Documentación',410),
                ('e6100000-0000-4000-8000-000000000042'::uuid,'Impuestos',420),
                ('e6100000-0000-4000-8000-000000000043'::uuid,'Trámites de aduanas',430),
                ('e6100000-0000-4000-8000-000000000044'::uuid,'Bodegaje',440),
                ('e6100000-0000-4000-8000-000000000045'::uuid,'Permisos',450),
                ('e6100000-0000-4000-8000-000000000046'::uuid,'Seguro de carga',460),
                ('e6100000-0000-4000-8000-000000000047'::uuid,'Entrega en destino',470),
                ('e6100000-0000-4000-8000-000000000048'::uuid,'Almacén fiscal',480),
                ('e6100000-0000-4000-8000-000000000049'::uuid,'Embalaje',490),
                ('e6100000-0000-4000-8000-000000000050'::uuid,'Cargos en destino',500),
                ('e6100000-0000-4000-8000-000000000051'::uuid,'Cargos en origen de la línea naviera',510),
                ('e6100000-0000-4000-8000-000000000052'::uuid,'Traslado a almacén por inspección o aforo',520)
            )
            INSERT INTO pricing."RateTermItems" (id,text,sort_order,is_active,created_at_utc)
            SELECT d.id,d.text,d.sort_order,TRUE,NOW()
            FROM desired d
            WHERE NOT EXISTS (
                SELECT 1 FROM pricing."RateTermItems" t
                WHERE lower(trim(t.text)) = lower(trim(d.text))
            );

            INSERT INTO pricing."RateTermBlocks"
                (id,name,rate_type,shipment_mode,poe_id,poe_name,poe_code,incoterm_id,incoterm_name,incoterm_code,sort_order,is_active,created_at_utc,transport_modality,direction,route_key)
            VALUES
                ('e6200000-0000-4000-8000-000000000001','Manual CRC · Común',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,100,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000002','Manual CRC · EXW',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000001','EXW','EXW',110,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000003','Manual CRC · FCA',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000002','FCA','FCA',110,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000004','Manual CRC · FOB',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000004','FOB','FOB',110,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000010','Manual CRC · Marítimo LCL Importación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Maritime','Importación',NULL),
                ('e6200000-0000-4000-8000-000000000011','Manual CRC · Marítimo LCL Exportación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Maritime','Exportación',NULL),
                ('e6200000-0000-4000-8000-000000000012','Manual CRC · Miami USA LCL Importación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,130,TRUE,NOW(),'Maritime','Importación','miami'),
                ('e6200000-0000-4000-8000-000000000020','Manual CRC · Marítimo FCL Importación',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Maritime','Importación',NULL),
                ('e6200000-0000-4000-8000-000000000021','Manual CRC · FCL Importación Caldera',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,130,TRUE,NOW(),'Maritime','Importación','caldera'),
                ('e6200000-0000-4000-8000-000000000022','Manual CRC · FCL Importación Moín',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,130,TRUE,NOW(),'Maritime','Importación','moin'),
                ('e6200000-0000-4000-8000-000000000023','Manual CRC · Marítimo FCL Exportación',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Maritime','Exportación',NULL),
                ('e6200000-0000-4000-8000-000000000030','Manual CRC · Terrestre LTL Exportación',NULL,'Ltl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Land','Exportación',NULL),
                ('e6200000-0000-4000-8000-000000000031','Manual CRC · Terrestre FTL Exportación',NULL,'Ftl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Land','Exportación',NULL),
                ('e6200000-0000-4000-8000-000000000032','Manual CRC · Terrestre FTL Importación',NULL,'Ftl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Land','Importación',NULL),
                ('e6200000-0000-4000-8000-000000000040','Manual CRC · Aéreo LCL Importación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Air','Importación',NULL),
                ('e6200000-0000-4000-8000-000000000041','Manual CRC · Aéreo LCL Exportación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Air','Exportación',NULL),
                ('e6200000-0000-4000-8000-000000000050','Manual CRC · Multimodal Panamá Importación',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,120,TRUE,NOW(),'Multimodal','Importación','panama'),
                ('e6200000-0000-4000-8000-000000000051','Manual CRC · China · Reglas generales',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,125,TRUE,NOW(),NULL,NULL,'china'),
                ('e6200000-0000-4000-8000-000000000100','Servicio · Recolección',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000101','Servicio · Trámite exportación',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000102','Servicio · Aduanas CRC',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000103','Servicio · Bodegaje',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000104','Servicio · Embalaje',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000105','Servicio · Seguro de carga',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL),
                ('e6200000-0000-4000-8000-000000000106','Servicio · Transporte internacional',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL,NULL)
            ON CONFLICT (id) DO UPDATE SET
                name=EXCLUDED.name,
                shipment_mode=EXCLUDED.shipment_mode,
                incoterm_id=EXCLUDED.incoterm_id,
                incoterm_name=EXCLUDED.incoterm_name,
                incoterm_code=EXCLUDED.incoterm_code,
                sort_order=EXCLUDED.sort_order,
                is_active=TRUE,
                transport_modality=EXCLUDED.transport_modality,
                direction=EXCLUDED.direction,
                route_key=EXCLUDED.route_key,
                updated_at_utc=NOW();

            WITH desired(block_id, term_text, category, sort_order) AS (VALUES
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Inspecciones, revisiones y escáner','SubjectTo',10),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Mensajería','SubjectTo',20),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Cambios sin previo aviso','SubjectTo',30),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Impuestos','Excludes',100),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Trámites de aduanas','Excludes',110),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Bodegaje','Excludes',120),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Permisos','Excludes',130),
                ('e6200000-0000-4000-8000-000000000001'::uuid,'Seguro de carga','Excludes',140),

                ('e6200000-0000-4000-8000-000000000002'::uuid,'Recolección','Includes',10),
                ('e6200000-0000-4000-8000-000000000002'::uuid,'Trámite de exportación','Includes',20),
                ('e6200000-0000-4000-8000-000000000002'::uuid,'Cargos en origen','Includes',30),
                ('e6200000-0000-4000-8000-000000000003'::uuid,'Cargos en origen','Includes',10),
                ('e6200000-0000-4000-8000-000000000004'::uuid,'Recolección','Excludes',10),
                ('e6200000-0000-4000-8000-000000000004'::uuid,'Cargos en origen','Excludes',20),

                ('e6200000-0000-4000-8000-000000000010'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000010'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000010'::uuid,'Cargos en destino de la línea naviera','Includes',30),
                ('e6200000-0000-4000-8000-000000000010'::uuid,'HBL','Includes',40),
                ('e6200000-0000-4000-8000-000000000010'::uuid,'IVA de los cargos en destino','SubjectTo',100),
                ('e6200000-0000-4000-8000-000000000011'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000011'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000011'::uuid,'HBL','Includes',30),
                ('e6200000-0000-4000-8000-000000000011'::uuid,'Cargos en destino','Excludes',100),

                ('e6200000-0000-4000-8000-000000000012'::uuid,'Forwarding USD 50.00','Includes',10),
                ('e6200000-0000-4000-8000-000000000012'::uuid,'Bunker según tarifario Miami','Includes',20),
                ('e6200000-0000-4000-8000-000000000012'::uuid,'THC/D según tarifario Miami','Includes',30),
                ('e6200000-0000-4000-8000-000000000012'::uuid,'SED USD 25 por factura de proveedor mayor a USD 2,500.00','SubjectTo',40),
                ('e6200000-0000-4000-8000-000000000012'::uuid,'Carga peligrosa USD 250.00','SubjectTo',50),

                ('e6200000-0000-4000-8000-000000000020'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Cargos en destino de la línea naviera','Includes',30),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'HBL','Includes',40),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Muellaje','SubjectTo',100),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Anticipado o Redestino según puerto y naviera','SubjectTo',110),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Marchamo','SubjectTo',120),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Retiro vacío','SubjectTo',130),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Sobrepeso / Patio','SubjectTo',140),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Demoras de chasis','SubjectTo',150),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Demoras de contenedor','SubjectTo',160),
                ('e6200000-0000-4000-8000-000000000020'::uuid,'Carta de transbordo','SubjectTo',170),
                ('e6200000-0000-4000-8000-000000000021'::uuid,'Inland Puerto Caldera a SJO','Includes',10),
                ('e6200000-0000-4000-8000-000000000021'::uuid,'Carrusel','SubjectTo',20),
                ('e6200000-0000-4000-8000-000000000022'::uuid,'Inland Puerto Moín a SJO','Includes',10),
                ('e6200000-0000-4000-8000-000000000023'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000023'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000023'::uuid,'HBL','Includes',30),
                ('e6200000-0000-4000-8000-000000000023'::uuid,'Cargos en origen de la línea naviera','Excludes',100),
                ('e6200000-0000-4000-8000-000000000023'::uuid,'Traslado a almacén por inspección o aforo','Excludes',110),

                ('e6200000-0000-4000-8000-000000000030'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000030'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000030'::uuid,'Carta Porte USD 40.00','Includes',30),
                ('e6200000-0000-4000-8000-000000000030'::uuid,'Manifiesto de Carga USD 40.00','Includes',40),
                ('e6200000-0000-4000-8000-000000000030'::uuid,'DUCA-T USD 40.00','Includes',50),
                ('e6200000-0000-4000-8000-000000000030'::uuid,'DUCA-F USD 40.00 si aplica','SubjectTo',60),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Trámite de exportación','Includes',20),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Impuesto de exportación terrestre USD 28.00','Includes',30),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Manejos','Includes',40),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Carta Porte USD 40.00','Includes',50),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'Manifiesto de Carga USD 40.00','Includes',60),
                ('e6200000-0000-4000-8000-000000000031'::uuid,'DUCA-T USD 40.00','Includes',70),
                ('e6200000-0000-4000-8000-000000000032'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000032'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000032'::uuid,'Carta Porte USD 40.00','Includes',30),
                ('e6200000-0000-4000-8000-000000000032'::uuid,'Manifiesto de Carga USD 40.00','Includes',40),
                ('e6200000-0000-4000-8000-000000000032'::uuid,'DUCA-T USD 40.00','Includes',50),

                ('e6200000-0000-4000-8000-000000000040'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000040'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000040'::uuid,'Cargos en destino de la aerolínea','Includes',30),
                ('e6200000-0000-4000-8000-000000000040'::uuid,'HAWB','Includes',40),
                ('e6200000-0000-4000-8000-000000000040'::uuid,'Retiro de guía aérea en destino USD 65 + IVA','SubjectTo',100),
                ('e6200000-0000-4000-8000-000000000040'::uuid,'IVA de los cargos en destino','SubjectTo',110),
                ('e6200000-0000-4000-8000-000000000041'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000041'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000041'::uuid,'HAWB','Includes',30),
                ('e6200000-0000-4000-8000-000000000041'::uuid,'Cargos en destino','Excludes',100),

                ('e6200000-0000-4000-8000-000000000050'::uuid,'Flete internacional','Includes',10),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'Manejos','Includes',20),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'HUB de transbordo en Panamá','Includes',30),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'Inland Panamá a Costa Rica','Includes',40),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'Documentación','Includes',50),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'Certificado de reexportación USD 125.00','SubjectTo',100),
                ('e6200000-0000-4000-8000-000000000050'::uuid,'No sobrepeso','SubjectTo',110),
                ('e6200000-0000-4000-8000-000000000051'::uuid,'Cambios sin previo aviso','SubjectTo',10),
                ('e6200000-0000-4000-8000-000000000051'::uuid,'Inspecciones, revisiones y escáner','SubjectTo',20),

                ('e6200000-0000-4000-8000-000000000100'::uuid,'Recolección','Includes',10),
                ('e6200000-0000-4000-8000-000000000101'::uuid,'Trámite de exportación','Includes',10),
                ('e6200000-0000-4000-8000-000000000102'::uuid,'Trámites de aduanas','Includes',10),
                ('e6200000-0000-4000-8000-000000000103'::uuid,'Bodegaje','Includes',10),
                ('e6200000-0000-4000-8000-000000000104'::uuid,'Embalaje','Includes',10),
                ('e6200000-0000-4000-8000-000000000105'::uuid,'Seguro de carga','Includes',10),
                ('e6200000-0000-4000-8000-000000000106'::uuid,'Flete internacional','Includes',10)
            )
            INSERT INTO pricing."RateTermBlockItems" (block_id,rate_term_item_id,category,sort_order)
            SELECT d.block_id,t.id,d.category,d.sort_order
            FROM desired d
            JOIN LATERAL (
                SELECT id
                FROM pricing."RateTermItems" t0
                WHERE lower(trim(t0.text)) = lower(trim(d.term_text))
                ORDER BY t0.is_active DESC, t0.created_at_utc, t0.id
                LIMIT 1
            ) t ON TRUE
            ON CONFLICT (block_id,rate_term_item_id) DO UPDATE SET
                category=EXCLUDED.category,
                sort_order=EXCLUDED.sort_order;

            WITH desired(block_id,service_code) AS (VALUES
                ('e6200000-0000-4000-8000-000000000100'::uuid,'PICKUP'),
                ('e6200000-0000-4000-8000-000000000101'::uuid,'CUSTOMS_FOREIGN'),
                ('e6200000-0000-4000-8000-000000000102'::uuid,'CUSTOMS_CR'),
                ('e6200000-0000-4000-8000-000000000103'::uuid,'STORAGE'),
                ('e6200000-0000-4000-8000-000000000104'::uuid,'PACKING'),
                ('e6200000-0000-4000-8000-000000000105'::uuid,'CARGO_INSURANCE'),
                ('e6200000-0000-4000-8000-000000000106'::uuid,'INT_TRANSPORT')
            )
            INSERT INTO pricing."RateTermBlockServices" (block_id,service_code)
            SELECT block_id,service_code FROM desired
            ON CONFLICT (block_id,service_code) DO NOTHING;

            -- Los días libres no deben salir de un texto estático del manual. Se agregan
            -- dinámicamente desde la tarifa/naviera seleccionada en el wizard.
            DELETE FROM pricing."RateTermBlockItems" i
            USING pricing."RateTermItems" t
            WHERE i.rate_term_item_id = t.id
              AND (lower(t.text) LIKE '%días libres%' OR lower(t.text) LIKE '%dias libres%');
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM pricing."RateTermBlockServices"
            WHERE block_id::text LIKE 'e6200000-0000-4000-8000-%';

            DELETE FROM pricing."RateTermBlocks"
            WHERE id::text LIKE 'e6200000-0000-4000-8000-%';
            """
        );
    }
}
