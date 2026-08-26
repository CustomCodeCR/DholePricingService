using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260826221500_UseSequentialPricingRateCodes")]
public sealed class UseSequentialPricingRateCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
            DROP CONSTRAINT IF EXISTS ck_rate_headers_rate_code_format;
            """
        );

        migrationBuilder.Sql(
            """
            CREATE SEQUENCE IF NOT EXISTS pricing.rate_quote_consecutive
                AS BIGINT
                INCREMENT BY 1
                MINVALUE 1
                MAXVALUE 99999999999
                START WITH 1
                NO CYCLE;
            """
        );

        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                total_rates BIGINT;
                max_numeric_code BIGINT;
                starting_value BIGINT;
            BEGIN
                SELECT COUNT(*)
                INTO total_rates
                FROM pricing."RateHeaders";

                SELECT MAX(
                    CASE
                        WHEN rate_code ~ '^QUO-[0-9]{5}-[0-9]{6}$'
                            THEN REPLACE(SUBSTRING(rate_code FROM 5), '-', '')::BIGINT
                        ELSE NULL
                    END
                )
                INTO max_numeric_code
                FROM pricing."RateHeaders";

                starting_value := GREATEST(total_rates, COALESCE(max_numeric_code, 0));

                IF starting_value > 0 THEN
                    PERFORM setval('pricing.rate_quote_consecutive', starting_value, true);
                ELSE
                    PERFORM setval('pricing.rate_quote_consecutive', 1, false);
                END IF;
            END;
            $$;
            """
        );

        migrationBuilder.Sql(
            """
            ALTER TABLE pricing."RateHeaders"
            ADD CONSTRAINT ck_rate_headers_rate_code_format
            CHECK (
                rate_code ~ '^QUO-[A-Z0-9]{5}-[A-Z0-9]{6}$'
            );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SEQUENCE IF EXISTS pricing.rate_quote_consecutive;");
    }
}