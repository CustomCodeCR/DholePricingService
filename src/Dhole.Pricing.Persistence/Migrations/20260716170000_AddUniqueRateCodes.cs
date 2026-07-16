using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260716170000_AddUniqueRateCodes")]
public sealed class AddUniqueRateCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "container_quantity",
            schema: "pricing",
            table: "RateHeaders",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );

        migrationBuilder.AddColumn<string>(
            name: "rate_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "rate_name",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: ""
        );

        migrationBuilder.AddColumn<int>(
            name: "quantity",
            schema: "pricing",
            table: "RateDetails",
            type: "integer",
            nullable: false,
            defaultValue: 1
        );

        migrationBuilder.Sql(
            """
            CREATE SEQUENCE pricing.rate_code_sequence
                AS BIGINT
                INCREMENT BY 1
                MINVALUE 1
                MAXVALUE 2176782335
                START WITH 1
                NO CYCLE;
            """
        );

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION pricing.to_base36(input_value BIGINT)
            RETURNS TEXT
            LANGUAGE plpgsql
            IMMUTABLE
            STRICT
            AS $$
            DECLARE
                alphabet CONSTANT TEXT := '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
                current_value BIGINT := input_value;
                result TEXT := '';
            BEGIN
                IF current_value = 0 THEN
                    RETURN '0';
                END IF;

                WHILE current_value > 0 LOOP
                    result :=
                        SUBSTRING(
                            alphabet,
                            ((current_value % 36)::INTEGER + 1),
                            1
                        ) || result;

                    current_value := current_value / 36;
                END LOOP;

                RETURN result;
            END;
            $$;
            """
        );

        migrationBuilder.Sql(
            """
            WITH numbered_rates AS
            (
                SELECT
                    id,
                    ROW_NUMBER() OVER (ORDER BY created_at_utc, id) AS consecutive
                FROM pricing."RateHeaders"
            )
            UPDATE pricing."RateHeaders" AS rate
            SET
                rate_code =
                    'QUO-' || LPAD(pricing.to_base36(numbered.consecutive), 6, '0'),
                rate_name =
                    'QUO-' || LPAD(pricing.to_base36(numbered.consecutive), 6, '0') ||
                    ' - Tarifa ' || rate.container_quantity ||
                    ' x ' || rate.container_type_name ||
                    ' - FOB - ' || rate.pol_name ||
                    ' To ' || rate.pod_name ||
                    ' Via ' ||
                    CASE
                        WHEN LOWER(rate.poe_name) LIKE '%caldera%' THEN 'Caldera'
                        WHEN LOWER(rate.poe_name) LIKE '%limon%'
                            OR LOWER(rate.poe_name) LIKE '%limón%'
                            OR LOWER(rate.poe_name) LIKE '%moin%'
                            OR LOWER(rate.poe_name) LIKE '%moín%' THEN 'Limón/Moín'
                        WHEN LOWER(rate.poe_name) LIKE '%manzanillo%'
                            OR LOWER(rate.poe_name) LIKE '%colon%'
                            OR LOWER(rate.poe_name) LIKE '%colón%'
                            OR LOWER(rate.poe_name) LIKE '%rodman%'
                            OR LOWER(rate.poe_name) LIKE '%cristobal%'
                            OR LOWER(rate.poe_name) LIKE '%cristóbal%' THEN 'Multimodal'
                        ELSE 'Desconocida'
                    END
            FROM numbered_rates AS numbered
            WHERE rate.id = numbered.id;
            """
        );

        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                existing_count BIGINT;
            BEGIN
                SELECT COUNT(*)
                INTO existing_count
                FROM pricing."RateHeaders";

                IF existing_count = 0 THEN
                    PERFORM setval('pricing.rate_code_sequence', 1, false);
                ELSE
                    PERFORM setval('pricing.rate_code_sequence', existing_count, true);
                END IF;
            END;
            $$;
            """
        );

        migrationBuilder.Sql(
            """
            DROP FUNCTION pricing.to_base36(BIGINT);
            """
        );

        migrationBuilder.AlterColumn<string>(
            name: "rate_name",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(500)",
            maxLength: 500,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500,
            oldDefaultValue: ""
        );

        migrationBuilder.AlterColumn<string>(
            name: "rate_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(10)",
            oldMaxLength: 10,
            oldNullable: true
        );

        migrationBuilder.CreateIndex(
            name: "ux_rate_headers_rate_code",
            schema: "pricing",
            table: "RateHeaders",
            column: "rate_code",
            unique: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_rate_headers_rate_code",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "container_quantity",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "rate_code",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "rate_name",
            schema: "pricing",
            table: "RateHeaders"
        );

        migrationBuilder.DropColumn(
            name: "quantity",
            schema: "pricing",
            table: "RateDetails"
        );

        migrationBuilder.Sql("DROP SEQUENCE IF EXISTS pricing.rate_code_sequence;");
    }
}
