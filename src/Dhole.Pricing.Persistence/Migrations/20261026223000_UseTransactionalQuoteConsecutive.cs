using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261026223000_UseTransactionalQuoteConsecutive")]
public sealed class UseTransactionalQuoteConsecutive : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing.rate_quote_counter (
                id SMALLINT PRIMARY KEY CHECK (id = 1),
                last_number BIGINT NOT NULL
                    CHECK (last_number >= 0 AND last_number <= 99999999999)
            );

            -- Solo códigos persistidos: no se heredan los números consumidos
            -- por nextval() en intentos fallidos.
            INSERT INTO pricing.rate_quote_counter (id, last_number)
            SELECT
                1,
                COALESCE(MAX(
                    CASE WHEN rate_code ~ '^QUO-[0-9]{5}-[0-9]{6}$'
                        THEN REPLACE(SUBSTRING(rate_code FROM 5), '-', '')::BIGINT
                        ELSE NULL
                    END
                ), 0)
            FROM pricing."RateHeaders"
            ON CONFLICT (id) DO UPDATE
            SET last_number = GREATEST(
                pricing.rate_quote_counter.last_number,
                EXCLUDED.last_number
            );
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Al revertir la aplicación, posicionar la secuencia antigua por encima
        // del último QUO emitido para que no se duplique ningún código.
        migrationBuilder.Sql(
            """
            SELECT setval(
                'pricing.rate_quote_consecutive',
                GREATEST((SELECT last_number FROM pricing.rate_quote_counter WHERE id = 1), 1),
                (SELECT last_number > 0 FROM pricing.rate_quote_counter WHERE id = 1)
            );

            DROP TABLE IF EXISTS pricing.rate_quote_counter;
            """
        );
    }
}
