using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260825233000_AddCostaRicaCommercialTermRules")]
public sealed class AddCostaRicaCommercialTermRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateTermBlocks"
                ADD COLUMN IF NOT EXISTS transport_modality character varying(30) NULL,
                ADD COLUMN IF NOT EXISTS direction character varying(30) NULL;

            CREATE TABLE IF NOT EXISTS pricing."RateTermBlockServices"
            (
                block_id uuid NOT NULL,
                service_code character varying(80) NOT NULL,
                CONSTRAINT "PK_RateTermBlockServices" PRIMARY KEY (block_id, service_code),
                CONSTRAINT "FK_RateTermBlockServices_RateTermBlocks_block_id"
                    FOREIGN KEY (block_id) REFERENCES pricing."RateTermBlocks"(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_RateTermBlockServices_service_code"
                ON pricing."RateTermBlockServices" (service_code);

            CREATE INDEX IF NOT EXISTS "IX_RateTermBlocks_commercial_lookup"
                ON pricing."RateTermBlocks" (transport_modality, shipment_mode, direction, incoterm_id, is_active);

            INSERT INTO pricing."RateTermBlocks"
                (id,name,rate_type,shipment_mode,poe_id,poe_name,poe_code,incoterm_id,incoterm_name,incoterm_code,sort_order,is_active,created_at_utc,transport_modality,direction)
            VALUES
                ('d1000000-0000-4000-8000-000000000001','CRC · Condiciones comunes',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,10,TRUE,NOW(),NULL,NULL),
                ('d1000000-0000-4000-8000-000000000002','CRC · EXW',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000001','EXW','EXW',20,TRUE,NOW(),NULL,NULL),
                ('d1000000-0000-4000-8000-000000000003','CRC · FCA',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000002','FCA','FCA',20,TRUE,NOW(),NULL,NULL),
                ('d1000000-0000-4000-8000-000000000004','CRC · FOB',NULL,NULL,NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000004','FOB','FOB',20,TRUE,NOW(),NULL,NULL),
                ('d1000000-0000-4000-8000-000000000010','CRC · Marítimo LCL',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Maritime',NULL),
                ('d1000000-0000-4000-8000-000000000011','CRC · Marítimo LCL Importación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,35,TRUE,NOW(),'Maritime','Importación'),
                ('d1000000-0000-4000-8000-000000000012','CRC · Marítimo LCL Exportación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,35,TRUE,NOW(),'Maritime','Exportación'),
                ('d1000000-0000-4000-8000-000000000020','CRC · Marítimo FCL',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Maritime',NULL),
                ('d1000000-0000-4000-8000-000000000021','CRC · Marítimo FCL Importación',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,35,TRUE,NOW(),'Maritime','Importación'),
                ('d1000000-0000-4000-8000-000000000022','CRC · Marítimo FCL Exportación',NULL,'Fcl',NULL,NULL,NULL,NULL,NULL,NULL,35,TRUE,NOW(),'Maritime','Exportación'),
                ('d1000000-0000-4000-8000-000000000030','CRC · Aéreo LCL',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Air',NULL),
                ('d1000000-0000-4000-8000-000000000031','CRC · Aéreo LCL Importación',NULL,'Lcl',NULL,NULL,NULL,NULL,NULL,NULL,35,TRUE,NOW(),'Air','Importación'),
                ('d1000000-0000-4000-8000-000000000032','CRC · Aéreo FCA',NULL,'Lcl',NULL,NULL,NULL,'c2500000-0000-4000-8000-000000000002','FCA','FCA',36,TRUE,NOW(),'Air',NULL),
                ('d1000000-0000-4000-8000-000000000040','CRC · Terrestre LTL',NULL,'Ltl',NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Land',NULL),
                ('d1000000-0000-4000-8000-000000000041','CRC · Terrestre FTL',NULL,'Ftl',NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Land',NULL),
                ('d1000000-0000-4000-8000-000000000050','CRC · Multimodal Panamá',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,30,TRUE,NOW(),'Multimodal','Importación')
            ON CONFLICT (id) DO UPDATE SET
                name=EXCLUDED.name, shipment_mode=EXCLUDED.shipment_mode, incoterm_id=EXCLUDED.incoterm_id,
                incoterm_name=EXCLUDED.incoterm_name, incoterm_code=EXCLUDED.incoterm_code,
                sort_order=EXCLUDED.sort_order, is_active=TRUE,
                transport_modality=EXCLUDED.transport_modality, direction=EXCLUDED.direction,
                updated_at_utc=NOW();

            WITH desired(block_id,item_id,category,sort_order) AS (VALUES
                ('d1000000-0000-4000-8000-000000000001'::uuid,'d2b17d0f-9646-4651-8720-e0aea9e0c1d2'::uuid,'SubjectTo',10),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'29b37f26-453b-4235-946f-15eb8c81f451'::uuid,'SubjectTo',20),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'b87226b4-38cd-43f8-9b36-31b00797c5b1'::uuid,'SubjectTo',30),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'600705bd-0f6f-4492-acc3-d4efd96bbc9f'::uuid,'SubjectTo',40),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'6c13ff4e-7820-4f93-9754-15567e1a081d'::uuid,'SubjectTo',50),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'563a6e47-7fbf-4f8a-94a5-64c5998ddd9e'::uuid,'Excludes',100),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'1a940376-d75c-49d0-88c0-010fa14e3bab'::uuid,'Excludes',110),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'3f7ea083-42aa-4f06-a0fe-f9c61ab8730e'::uuid,'Excludes',120),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'cdea5aac-53e4-4937-bb8c-e8877444ae21'::uuid,'Excludes',130),
                ('d1000000-0000-4000-8000-000000000001'::uuid,'f5c9663e-6d8f-4aea-85fa-2e7634ad8c1c'::uuid,'Excludes',140),

                ('d1000000-0000-4000-8000-000000000002'::uuid,'87107485-3fb8-4bc8-88bc-28e357c10450'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000002'::uuid,'10802df5-1ec8-4d24-ac7c-2d2b51092ec7'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000002'::uuid,'619519e9-7ccb-4305-8be4-86c1dc36a3ec'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000003'::uuid,'87107485-3fb8-4bc8-88bc-28e357c10450'::uuid,'Excludes',10),
                ('d1000000-0000-4000-8000-000000000003'::uuid,'500f1517-13fa-42c8-967c-5dff98dbea45'::uuid,'Excludes',20),
                ('d1000000-0000-4000-8000-000000000004'::uuid,'87107485-3fb8-4bc8-88bc-28e357c10450'::uuid,'Excludes',10),
                ('d1000000-0000-4000-8000-000000000004'::uuid,'619519e9-7ccb-4305-8be4-86c1dc36a3ec'::uuid,'Excludes',20),

                ('d1000000-0000-4000-8000-000000000010'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000010'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000010'::uuid,'6eece707-c80e-46f5-bfd8-54c30271acda'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000011'::uuid,'c3dfe13a-30d4-407e-821e-6a9f519ceed7'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000011'::uuid,'6ddaea34-9a7d-4335-8543-a328a9805c1c'::uuid,'SubjectTo',20),
                ('d1000000-0000-4000-8000-000000000011'::uuid,'21bf7cd9-ece8-4b97-b4d3-5283f9611bfb'::uuid,'SubjectTo',30),
                ('d1000000-0000-4000-8000-000000000011'::uuid,'ab6a5efa-24b7-4fa2-b07c-80f055bcc95a'::uuid,'SubjectTo',40),
                ('d1000000-0000-4000-8000-000000000011'::uuid,'c98c4948-3286-46f4-9def-df303016ea8b'::uuid,'SubjectTo',50),
                ('d1000000-0000-4000-8000-000000000012'::uuid,'7f922d58-5b01-4de6-967a-dfa4ef42018b'::uuid,'Excludes',10),

                ('d1000000-0000-4000-8000-000000000020'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'6eece707-c80e-46f5-bfd8-54c30271acda'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'2ec34614-1eb2-4bb1-9520-b3a1bce76622'::uuid,'SubjectTo',100),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'08952c7d-d721-4284-ad86-3ba311089bc6'::uuid,'SubjectTo',110),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'b10e0465-8d30-4321-8a23-7e4aea847161'::uuid,'SubjectTo',120),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'acd5904f-3ffe-46a1-9e91-da67cfe952c3'::uuid,'SubjectTo',130),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'001d55b0-cd27-420d-b8be-7266036768aa'::uuid,'SubjectTo',140),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'b7ea884f-a114-49d8-a270-b93129828f95'::uuid,'SubjectTo',150),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'41a0e774-197f-4f0c-b3ce-4228032c906a'::uuid,'SubjectTo',160),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'7b95c6a6-82b3-43aa-9975-50df8310ec7b'::uuid,'SubjectTo',170),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'15d5b294-8c60-4a8f-b515-05d5e6ccdf09'::uuid,'SubjectTo',180),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'d49f9695-c43f-4c98-834a-0f038ae1f9a6'::uuid,'SubjectTo',190),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'34f3b077-6f81-4842-b98d-032cc94712cc'::uuid,'SubjectTo',200),
                ('d1000000-0000-4000-8000-000000000020'::uuid,'2e1139d8-38ad-4e3b-8a11-aa83ad641dc7b'::uuid,'SubjectTo',210),
                ('d1000000-0000-4000-8000-000000000021'::uuid,'c3dfe13a-30d4-407e-821e-6a9f519ceed7'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000021'::uuid,'b6842365-4bbc-4efa-8316-8b54c9bb384d'::uuid,'Includes',15),
                ('d1000000-0000-4000-8000-000000000021'::uuid,'6ddaea34-9a7d-4335-8543-a328a9805c1c'::uuid,'SubjectTo',20),
                ('d1000000-0000-4000-8000-000000000021'::uuid,'e87adf74-8f91-4072-962d-2185dbbf4828'::uuid,'Excludes',30),
                ('d1000000-0000-4000-8000-000000000022'::uuid,'a10eea66-7ca0-4fce-87e7-402cf3e1bac1'::uuid,'Excludes',10),
                ('d1000000-0000-4000-8000-000000000022'::uuid,'b77ad417-8577-4810-aa73-b6c5cf7384d9'::uuid,'Excludes',20),

                ('d1000000-0000-4000-8000-000000000030'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000030'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000030'::uuid,'59aa3bbb-da40-41ad-8eda-848632a59d8b'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000031'::uuid,'185a85f7-4d38-4488-a2fe-ee3aa841370d'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000031'::uuid,'6ddaea34-9a7d-4335-8543-a328a9805c1c'::uuid,'SubjectTo',20),
                ('d1000000-0000-4000-8000-000000000031'::uuid,'6a0e63f0-81d6-4ceb-a613-94348a5d6c3a'::uuid,'SubjectTo',30),
                ('d1000000-0000-4000-8000-000000000032'::uuid,'a50d526c-4805-49f6-8956-b699df761905'::uuid,'Includes',10),

                ('d1000000-0000-4000-8000-000000000040'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000040'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000040'::uuid,'097f016a-3484-4951-9aaa-405d4212637b'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000040'::uuid,'c678240e-bf7a-4c13-a2f2-7bcd5fb87375'::uuid,'Includes',40),
                ('d1000000-0000-4000-8000-000000000040'::uuid,'80f6ba98-c333-4b1b-a3b4-83707120d91b'::uuid,'Includes',50),
                ('d1000000-0000-4000-8000-000000000040'::uuid,'9073dbca-5b65-43a2-af44-ec2be2dade6b'::uuid,'SubjectTo',60),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'097f016a-3484-4951-9aaa-405d4212637b'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'c678240e-bf7a-4c13-a2f2-7bcd5fb87375'::uuid,'Includes',40),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'80f6ba98-c333-4b1b-a3b4-83707120d91b'::uuid,'Includes',50),
                ('d1000000-0000-4000-8000-000000000041'::uuid,'9073dbca-5b65-43a2-af44-ec2be2dade6b'::uuid,'SubjectTo',60),

                ('d1000000-0000-4000-8000-000000000050'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'730c1bdd-73a8-45ca-bbb1-1f83aa9c0f19'::uuid,'Includes',20),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'3b6500bf-77bd-43ec-bdbe-9f39e87dab9c'::uuid,'Includes',30),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'43a7f067-d6be-4338-8fe1-12738ea5d0e4'::uuid,'Includes',40),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'bacd9cd6-75d2-4ce4-b0ec-1858781e8498'::uuid,'Includes',50),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'8a3f7880-7245-426e-8e7a-fa553bef936f'::uuid,'Includes',60),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'f23dab31-3571-43c5-83e0-8e4d8143325e'::uuid,'Includes',70),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'d3753315-1e0b-43bf-88c2-ea805ea0e0df'::uuid,'SubjectTo',80),
                ('d1000000-0000-4000-8000-000000000050'::uuid,'30a5d747-0c58-4980-ad36-ccf79efb972a'::uuid,'SubjectTo',90)
            )
            INSERT INTO pricing."RateTermBlockItems" (block_id,rate_term_item_id,category,sort_order)
            SELECT d.block_id,d.item_id,d.category,d.sort_order
            FROM desired d
            JOIN pricing."RateTermItems" t ON t.id=d.item_id
            ON CONFLICT (block_id,rate_term_item_id) DO UPDATE
                SET category=EXCLUDED.category, sort_order=EXCLUDED.sort_order;

            INSERT INTO pricing."RateTermBlocks"
                (id,name,rate_type,shipment_mode,poe_id,poe_name,poe_code,incoterm_id,incoterm_name,incoterm_code,sort_order,is_active,created_at_utc,transport_modality,direction)
            VALUES
                ('d2000000-0000-4000-8000-000000000001','Servicio · Transporte internacional',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000002','Servicio · Aduanas Costa Rica',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000003','Servicio · Aduanas exterior',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000004','Servicio · Almacenamiento',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000005','Servicio · Seguro de carga',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000006','Servicio · Embalaje',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000007','Servicio · Recolección',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000008','Servicio · Recepción de carga',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL),
                ('d2000000-0000-4000-8000-000000000009','Condición · Carga peligrosa',NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,200,TRUE,NOW(),NULL,NULL)
            ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,is_active=TRUE,updated_at_utc=NOW();

            INSERT INTO pricing."RateTermBlockServices" (block_id,service_code) VALUES
                ('d2000000-0000-4000-8000-000000000001','INT_TRANSPORT'),
                ('d2000000-0000-4000-8000-000000000002','CUSTOMS_CR'),
                ('d2000000-0000-4000-8000-000000000003','CUSTOMS_FOREIGN'),
                ('d2000000-0000-4000-8000-000000000004','STORAGE'),
                ('d2000000-0000-4000-8000-000000000005','CARGO_INSURANCE'),
                ('d2000000-0000-4000-8000-000000000006','PACKING'),
                ('d2000000-0000-4000-8000-000000000007','PICKUP'),
                ('d2000000-0000-4000-8000-000000000008','RECEPTION'),
                ('d2000000-0000-4000-8000-000000000009','DANGEROUS_CARGO')
            ON CONFLICT DO NOTHING;

            WITH desired(block_id,item_id,category,sort_order) AS (VALUES
                ('d2000000-0000-4000-8000-000000000001'::uuid,'f2aa5290-4223-4eb3-af50-b74cbdc78d7a'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000002'::uuid,'1a940376-d75c-49d0-88c0-010fa14e3bab'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000003'::uuid,'10802df5-1ec8-4d24-ac7c-2d2b51092ec7'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000004'::uuid,'3f7ea083-42aa-4f06-a0fe-f9c61ab8730e'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000005'::uuid,'f5c9663e-6d8f-4aea-85fa-2e7634ad8c1c'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000006'::uuid,'500f1517-13fa-42c8-967c-5dff98dbea45'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000007'::uuid,'87107485-3fb8-4bc8-88bc-28e357c10450'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000008'::uuid,'f23dab31-3571-43c5-83e0-8e4d8143325e'::uuid,'Includes',10),
                ('d2000000-0000-4000-8000-000000000009'::uuid,'6efa593a-8a1f-42d3-a275-be4f4201004f'::uuid,'SubjectTo',10)
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
            DELETE FROM pricing."RateTermBlocks"
            WHERE id::text LIKE 'd1000000-%' OR id::text LIKE 'd2000000-%';
            DROP TABLE IF EXISTS pricing."RateTermBlockServices";
            DROP INDEX IF EXISTS pricing."IX_RateTermBlocks_commercial_lookup";
            ALTER TABLE pricing."RateTermBlocks" DROP COLUMN IF EXISTS transport_modality;
            ALTER TABLE pricing."RateTermBlocks" DROP COLUMN IF EXISTS direction;
            """
        );
    }
}
