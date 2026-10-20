using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261020150000_AddLclGroupAgent")]
public sealed class AddLclGroupAgent : Migration
{
    protected override void Up(MigrationBuilder builder)
    {
        builder.AddColumn<Guid>(name: "lcl_group_agent_id", schema: "pricing", table: "RateHeaders",
            type: "uuid", nullable: true);
        builder.AddColumn<string>(name: "lcl_group_agent_name", schema: "pricing", table: "RateHeaders",
            type: "character varying(250)", maxLength: 250, nullable: true);
        builder.AddColumn<string>(name: "lcl_group_agent_code", schema: "pricing", table: "RateHeaders",
            type: "character varying(80)", maxLength: 80, nullable: true);
    }
    protected override void Down(MigrationBuilder builder)
    {
        builder.DropColumn(name: "lcl_group_agent_id", schema: "pricing", table: "RateHeaders");
        builder.DropColumn(name: "lcl_group_agent_name", schema: "pricing", table: "RateHeaders");
        builder.DropColumn(name: "lcl_group_agent_code", schema: "pricing", table: "RateHeaders");
    }
}
