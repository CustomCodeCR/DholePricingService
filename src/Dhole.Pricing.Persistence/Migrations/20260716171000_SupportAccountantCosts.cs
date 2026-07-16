using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260716171000_SupportAccountantCosts")]
public sealed class SupportAccountantCosts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs"
        );

        migrationBuilder.AlterColumn<Guid>(
            name: "port_id",
            schema: "pricing",
            table: "Costs",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid"
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_name",
            schema: "pricing",
            table: "Costs",
            type: "character varying(250)",
            maxLength: 250,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(250)",
            oldMaxLength: 250
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_code",
            schema: "pricing",
            table: "Costs",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(80)",
            oldMaxLength: 80
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_role",
            schema: "pricing",
            table: "Costs",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50
        );

        migrationBuilder.AddColumn<bool>(
            name: "is_accountant",
            schema: "pricing",
            table: "Costs",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateIndex(
            name: "IX_Costs_is_accountant",
            schema: "pricing",
            table: "Costs",
            column: "is_accountant"
        );

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type",
                "cost_detail_type",
                "carrier_id",
                "agent_id",
                "port_id",
                "port_role",
                "is_accountant",
                "name",
                "currency_id",
            },
            unique: true,
            filter: "is_deleted = false"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Costs_is_accountant",
            schema: "pricing",
            table: "Costs"
        );

        migrationBuilder.DropIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs"
        );

        migrationBuilder.DropColumn(
            name: "is_accountant",
            schema: "pricing",
            table: "Costs"
        );

        migrationBuilder.Sql(
            """
            DELETE FROM pricing."Costs"
            WHERE port_id IS NULL
               OR port_name IS NULL
               OR port_code IS NULL
               OR port_role IS NULL;
            """
        );

        migrationBuilder.AlterColumn<Guid>(
            name: "port_id",
            schema: "pricing",
            table: "Costs",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_name",
            schema: "pricing",
            table: "Costs",
            type: "character varying(250)",
            maxLength: 250,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(250)",
            oldMaxLength: 250,
            oldNullable: true
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_code",
            schema: "pricing",
            table: "Costs",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(80)",
            oldMaxLength: 80,
            oldNullable: true
        );

        migrationBuilder.AlterColumn<string>(
            name: "port_role",
            schema: "pricing",
            table: "Costs",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50,
            oldNullable: true
        );

        migrationBuilder.CreateIndex(
            name: "ix_costs_template_unique",
            schema: "pricing",
            table: "Costs",
            columns: new[]
            {
                "cost_type",
                "cost_detail_type",
                "carrier_id",
                "agent_id",
                "port_id",
                "port_role",
                "name",
                "currency_id",
            },
            unique: true,
            filter: "is_deleted = false"
        );
    }
}
