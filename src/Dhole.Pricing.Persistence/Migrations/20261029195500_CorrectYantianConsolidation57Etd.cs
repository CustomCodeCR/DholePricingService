using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20261029195500_CorrectYantianConsolidation57Etd")]
public sealed class CorrectYantianConsolidation57Etd : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The named sailing is 02 November 2026, but the persisted ETD was
        // accidentally saved as 02 October 2026. Pricing correctly rejects
        // expired departures; repair this one confirmed record, never relax
        // the date validation or silently alter other consolidations.
        migrationBuilder.Sql(
            """
            UPDATE pricing."OwnLclConsolidations"
            SET etd = DATE '2026-11-02',
                updated_at_utc = NOW()
            WHERE id = '1202b93f-ed16-487c-a8e2-737dcf972ece'::uuid
              AND consolidation_number = 57
              AND booking = '279812536'
              AND BTRIM(name) = 'CNYACA-001 ETD 02 de noviembre de 2026'
              AND UPPER(BTRIM(COALESCE(pol_name, ''))) = 'YANTIAN, CHINA'
              AND is_active = TRUE
              AND etd = DATE '2026-10-02';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair is not reversible: do not overwrite a sailing date
        // changed by an operator after this migration.
    }
}
