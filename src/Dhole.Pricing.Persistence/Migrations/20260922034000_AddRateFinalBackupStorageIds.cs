using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260922034000_AddRateFinalBackupStorageIds")]
public partial class AddRateFinalBackupStorageIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid[]>(
            name: "final_backup_storage_ids",
            schema: "pricing",
            table: "RateHeaders",
            type: "uuid[]",
            nullable: false,
            defaultValueSql: "'{}'::uuid[]"
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "final_backup_storage_ids",
            schema: "pricing",
            table: "RateHeaders"
        );
    }
}
