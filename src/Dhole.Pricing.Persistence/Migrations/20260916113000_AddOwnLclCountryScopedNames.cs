using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260916113000_AddOwnLclCountryScopedNames")]
public sealed class AddOwnLclCountryScopedNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."OwnLclConsolidations"
                ADD COLUMN IF NOT EXISTS origin_country varchar(120),
                ADD COLUMN IF NOT EXISTS country_sequence integer;

            UPDATE pricing."OwnLclConsolidations"
            SET origin_country = CASE
                    WHEN btrim(COALESCE(pol_name, '')) LIKE '%,%'
                        THEN NULLIF(btrim(regexp_replace(btrim(pol_name), '^.*,', '')), '')
                    WHEN upper(btrim(COALESCE(pol_code, ''))) IN
                        ('SHANGHAI','NINGBO','QINGDAO','XIAMEN','SHANTOU','DALIAN','CHONGQING','FUZHOU','SHENZHEN','XINGANG','SHEKOU','GUANGZHOU')
                        THEN 'China'
                    WHEN btrim(COALESCE(pol_name, '')) <> ''
                        THEN btrim(pol_name)
                    ELSE 'Origen'
                END,
                country_sequence = consolidation_number;

            UPDATE pricing."OwnLclConsolidations"
            SET origin_country = 'Origen'
            WHERE origin_country IS NULL OR btrim(origin_country) = '';

            UPDATE pricing."OwnLclConsolidations"
            SET name = CONCAT('Consolidado ', origin_country, ' ', country_sequence);

            ALTER TABLE pricing."OwnLclConsolidations"
                ALTER COLUMN origin_country SET NOT NULL,
                ALTER COLUMN country_sequence SET NOT NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_OwnLclConsolidations_OriginCountry_CountrySequence"
                ON pricing."OwnLclConsolidations" (lower(origin_country), country_sequence);

            CREATE OR REPLACE FUNCTION pricing.set_own_lcl_consolidation_country_name()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            DECLARE
                v_pol_name text;
                v_pol_code text;
                v_country text;
                v_next_sequence integer;
            BEGIN
                v_pol_name := btrim(COALESCE(NEW.pol_name, ''));
                v_pol_code := upper(btrim(COALESCE(NEW.pol_code, '')));

                IF v_pol_name LIKE '%,%' THEN
                    v_country := NULLIF(btrim(regexp_replace(v_pol_name, '^.*,', '')), '');
                ELSIF v_pol_code IN
                    ('SHANGHAI','NINGBO','QINGDAO','XIAMEN','SHANTOU','DALIAN','CHONGQING','FUZHOU','SHENZHEN','XINGANG','SHEKOU','GUANGZHOU') THEN
                    v_country := 'China';
                ELSIF v_pol_name <> '' THEN
                    v_country := v_pol_name;
                ELSE
                    v_country := 'Origen';
                END IF;

                IF v_country IS NULL OR btrim(v_country) = '' THEN
                    v_country := 'Origen';
                END IF;

                IF TG_OP = 'INSERT'
                   OR OLD.origin_country IS NULL
                   OR lower(OLD.origin_country) IS DISTINCT FROM lower(v_country)
                   OR OLD.country_sequence IS NULL THEN
                    PERFORM pg_advisory_xact_lock(hashtext(lower(v_country))::bigint);

                    SELECT COALESCE(MAX(country_sequence), 0) + 1
                    INTO v_next_sequence
                    FROM pricing."OwnLclConsolidations"
                    WHERE lower(origin_country) = lower(v_country);

                    NEW.country_sequence := v_next_sequence;
                END IF;

                NEW.origin_country := v_country;
                NEW.name := CONCAT('Consolidado ', v_country, ' ', NEW.country_sequence);
                RETURN NEW;
            END;
            $$;

            DROP TRIGGER IF EXISTS "TR_OwnLclConsolidations_CountryName"
                ON pricing."OwnLclConsolidations";

            CREATE TRIGGER "TR_OwnLclConsolidations_CountryName"
            BEFORE INSERT OR UPDATE OF pol_name, pol_code
            ON pricing."OwnLclConsolidations"
            FOR EACH ROW
            EXECUTE FUNCTION pricing.set_own_lcl_consolidation_country_name();
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS "TR_OwnLclConsolidations_CountryName"
                ON pricing."OwnLclConsolidations";

            DROP FUNCTION IF EXISTS pricing.set_own_lcl_consolidation_country_name();

            DROP INDEX IF EXISTS pricing."UX_OwnLclConsolidations_OriginCountry_CountrySequence";

            UPDATE pricing."OwnLclConsolidations"
            SET name = CONCAT('Consolidado ', consolidation_number);

            ALTER TABLE pricing."OwnLclConsolidations"
                DROP COLUMN IF EXISTS country_sequence,
                DROP COLUMN IF EXISTS origin_country;
            """
        );
    }
}
