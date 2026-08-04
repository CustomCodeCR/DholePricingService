using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260804103000_UseRandomAlphanumericQuoRateCodes")]
public sealed class UseRandomAlphanumericQuoRateCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "rate_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(10)",
            oldMaxLength: 10
        );

        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                current_rate RECORD;
                alphabet CONSTANT TEXT := '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
                generated_code TEXT;
                first_block TEXT;
                second_block TEXT;
                suffix TEXT;
                character_index INTEGER;
            BEGIN
                FOR current_rate IN
                    SELECT id, rate_name
                    FROM pricing."RateHeaders"
                    ORDER BY created_at_utc, id
                LOOP
                    LOOP
                        first_block := '';
                        second_block := '';

                        FOR character_index IN 1..5 LOOP
                            first_block := first_block || SUBSTRING(
                                alphabet,
                                FLOOR(random() * LENGTH(alphabet))::INTEGER + 1,
                                1
                            );
                        END LOOP;

                        FOR character_index IN 1..6 LOOP
                            second_block := second_block || SUBSTRING(
                                alphabet,
                                FLOOR(random() * LENGTH(alphabet))::INTEGER + 1,
                                1
                            );
                        END LOOP;

                        generated_code := 'QUO-' || first_block || '-' || second_block;

                        EXIT WHEN NOT EXISTS
                        (
                            SELECT 1
                            FROM pricing."RateHeaders"
                            WHERE rate_code = generated_code
                        );
                    END LOOP;

                    suffix :=
                        CASE
                            WHEN POSITION(' - ' IN current_rate.rate_name) > 0
                                THEN SUBSTRING(
                                    current_rate.rate_name
                                    FROM POSITION(' - ' IN current_rate.rate_name)
                                )
                            ELSE ''
                        END;

                    UPDATE pricing."RateHeaders"
                    SET
                        rate_code = generated_code,
                        rate_name = generated_code || suffix
                    WHERE id = current_rate.id;
                END LOOP;
            END;
            $$;
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
            ADD CONSTRAINT ck_rate_headers_rate_code_format
            CHECK (rate_code ~ '^QUO-[A-Z0-9]{5}-[A-Z0-9]{6}$');
            """
        );

        migrationBuilder.Sql("DROP SEQUENCE IF EXISTS pricing.rate_code_sequence;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
            DROP CONSTRAINT IF EXISTS ck_rate_headers_rate_code_format;
            """
        );

        migrationBuilder.Sql(
            """
            CREATE SEQUENCE IF NOT EXISTS pricing.rate_code_sequence
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
                        SUBSTRING(alphabet, ((current_value % 36)::INTEGER + 1), 1) || result;
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
                    rate_name,
                    ROW_NUMBER() OVER (ORDER BY created_at_utc, id) AS consecutive
                FROM pricing."RateHeaders"
            )
            UPDATE pricing."RateHeaders" AS rate
            SET
                rate_code =
                    'QUO-' || LPAD(pricing.to_base36(numbered.consecutive), 6, '0'),
                rate_name =
                    'QUO-' || LPAD(pricing.to_base36(numbered.consecutive), 6, '0') ||
                    CASE
                        WHEN POSITION(' - ' IN numbered.rate_name) > 0
                            THEN SUBSTRING(
                                numbered.rate_name
                                FROM POSITION(' - ' IN numbered.rate_name)
                            )
                        ELSE ''
                    END
            FROM numbered_rates AS numbered
            WHERE rate.id = numbered.id;
            """
        );

        migrationBuilder.Sql(
            """
            SELECT setval(
                'pricing.rate_code_sequence',
                GREATEST((SELECT COUNT(*) FROM pricing."RateHeaders"), 1),
                (SELECT COUNT(*) FROM pricing."RateHeaders") > 0
            );
            """
        );

        migrationBuilder.Sql("DROP FUNCTION pricing.to_base36(BIGINT);");

        migrationBuilder.AlterColumn<string>(
            name: "rate_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16
        );
    }
}
