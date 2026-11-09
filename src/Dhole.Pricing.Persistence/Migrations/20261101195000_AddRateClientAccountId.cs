using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261101195000_AddRateClientAccountId")]
public sealed class AddRateClientAccountId : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AddColumn<Guid>(name: "client_account_id", schema: "pricing",
            table: "RateHeaders", type: "uuid", nullable: true);
        builder.CreateIndex(name: "ix_rate_headers_client_account_id",
            schema: "pricing", table: "RateHeaders", column: "client_account_id");
        // Existing quotes remain unlinked until identity is established.
        // Matching by client name alone can expose another customer's history.
    }

    protected override void Down(MigrationBuilder builder)
    {
        builder.DropIndex(name: "ix_rate_headers_client_account_id",
            schema: "pricing", table: "RateHeaders");
        builder.DropColumn(name: "client_account_id",
            schema: "pricing", table: "RateHeaders");
    }
}
