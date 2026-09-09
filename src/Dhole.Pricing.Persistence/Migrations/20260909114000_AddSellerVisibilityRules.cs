using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

public partial class AddSellerVisibilityRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS pricing."SellerVisibilityRules"
            (
                viewer_user_id uuid NOT NULL,
                seller_user_id uuid NOT NULL,
                created_at_utc timestamp with time zone NOT NULL DEFAULT NOW(),
                created_by_user_id uuid NULL,
                CONSTRAINT "PK_SellerVisibilityRules" PRIMARY KEY (viewer_user_id, seller_user_id),
                CONSTRAINT "CK_SellerVisibilityRules_NoSelf" CHECK (viewer_user_id <> seller_user_id)
            );

            CREATE INDEX IF NOT EXISTS "IX_SellerVisibilityRules_seller_user_id"
                ON pricing."SellerVisibilityRules" (seller_user_id);
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS pricing."SellerVisibilityRules";
            """
        );
    }
}
