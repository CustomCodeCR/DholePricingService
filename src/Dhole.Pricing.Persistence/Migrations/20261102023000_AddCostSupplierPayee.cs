using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261102023000_AddCostSupplierPayee")]
public sealed class AddCostSupplierPayee : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS pricing."CostSupplierPayees"
            (
                cost_id uuid NOT NULL PRIMARY KEY,
                supplier_id uuid NOT NULL,
                supplier_name varchar(220) NOT NULL,
                supplier_code varchar(100),
                supplier_type varchar(60),
                updated_at_utc timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT "FK_CostSupplierPayees_Costs"
                    FOREIGN KEY (cost_id) REFERENCES pricing."Costs" (id)
                    ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_CostSupplierPayees_supplier_id"
                ON pricing."CostSupplierPayees" (supplier_id);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve existing accounting attribution on rollback.
    }
}
