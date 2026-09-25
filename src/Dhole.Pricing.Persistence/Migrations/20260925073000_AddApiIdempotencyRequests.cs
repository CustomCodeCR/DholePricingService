using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260925073000_AddApiIdempotencyRequests")]
public sealed class AddApiIdempotencyRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."ApiIdempotencyRequests"
            (
                key_hash varchar(64) PRIMARY KEY,
                request_hash varchar(64) NOT NULL,
                state varchar(16) NOT NULL,
                response_status integer NULL,
                response_body_base64 text NULL,
                response_content_type varchar(256) NULL,
                response_location text NULL,
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                completed_at_utc timestamptz NULL,
                expires_at_utc timestamptz NOT NULL,
                CONSTRAINT "CK_ApiIdempotencyRequests_state"
                    CHECK (state IN ('Processing', 'Completed'))
            );

            CREATE INDEX IF NOT EXISTS "IX_ApiIdempotencyRequests_expires_at_utc"
                ON pricing."ApiIdempotencyRequests" (expires_at_utc);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."ApiIdempotencyRequests";
            """
        );
    }
}
