using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261101213000_AddRateClientLeadId")]
public sealed class AddRateClientLeadId : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AddColumn<Guid>(name: "client_lead_id", schema: "pricing",
            table: "RateHeaders", type: "uuid", nullable: true);
        builder.CreateIndex(name: "ix_rate_headers_client_lead_id",
            schema: "pricing", table: "RateHeaders", column: "client_lead_id");
    }
    protected override void Down(MigrationBuilder builder)
    {
        builder.DropIndex(name: "ix_rate_headers_client_lead_id",
            schema: "pricing", table: "RateHeaders");
        builder.DropColumn(name: "client_lead_id",
            schema: "pricing", table: "RateHeaders");
    }
}
