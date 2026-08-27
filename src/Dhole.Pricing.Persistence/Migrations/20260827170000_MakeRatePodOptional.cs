using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260827170000_MakeRatePodOptional")]
public partial class MakeRatePodOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "pod_id",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "pod_name",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(250)",
            maxLength: 250,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(250)",
            oldMaxLength: 250);

        migrationBuilder.AlterColumn<string>(
            name: "pod_code",
            schema: "pricing",
            table: "RateHeaders",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(80)",
            oldMaxLength: 80);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE pricing.\"RateHeaders\" SET pod_id = '00000000-0000-0000-0000-000000000000' WHERE pod_id IS NULL;");
        migrationBuilder.Sql("UPDATE pricing.\"RateHeaders\" SET pod_name = '' WHERE pod_name IS NULL;");
        migrationBuilder.Sql("UPDATE pricing.\"RateHeaders\" SET pod_code = '' WHERE pod_code IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "pod_id", schema: "pricing", table: "RateHeaders", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "pod_name", schema: "pricing", table: "RateHeaders", type: "character varying(250)", maxLength: 250, nullable: false, oldClrType: typeof(string), oldType: "character varying(250)", oldMaxLength: 250, oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "pod_code", schema: "pricing", table: "RateHeaders", type: "character varying(80)", maxLength: 80, nullable: false, oldClrType: typeof(string), oldType: "character varying(80)", oldMaxLength: 80, oldNullable: true);
    }
}
