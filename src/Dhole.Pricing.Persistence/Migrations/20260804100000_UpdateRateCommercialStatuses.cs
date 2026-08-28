using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260804100000_UpdateRateCommercialStatuses")]
public sealed class UpdateRateCommercialStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Algunas instalaciones antiguas de Pricing fueron creadas con
            -- RateHeaders.status como varchar(10). Los estados comerciales
            -- actuales (por ejemplo ApprovedByManagement/AcceptedByClient)
            -- exceden ese largo y PostgreSQL falla con 22001 antes de poder
            -- completar esta migración. Normalizamos primero la columna al
            -- largo que define actualmente RateHeaderConfiguration.
            ALTER TABLE pricing."RateHeaders"
            ALTER COLUMN status TYPE character varying(50)
            USING status::character varying(50);

            UPDATE pricing."RateHeaders"
            SET status = CASE
                WHEN status <> 'RejectedByClient'
                    AND NULLIF(BTRIM(idtra_number), '') IS NOT NULL
                    AND NULLIF(BTRIM(quo_number), '') IS NOT NULL
                    THEN 'AcceptedByClient'
                WHEN status = 'Approved' AND margin_percentage < 12
                    THEN 'ApprovedByManagement'
                WHEN status IN ('Approved', 'Draft', 'PendingApproval') AND margin_percentage >= 12
                    THEN 'Open'
                WHEN status = 'Rejected'
                    THEN 'RejectedByManagement'
                WHEN status = 'Draft'
                    THEN 'PendingApproval'
                ELSE status
            END;

            UPDATE pricing."RateHeaders"
            SET required_approval = (status = 'PendingApproval');
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE pricing."RateHeaders"
            SET status = CASE status
                WHEN 'ApprovedByManagement' THEN 'Approved'
                WHEN 'RejectedByManagement' THEN 'Rejected'
                WHEN 'Open' THEN 'Approved'
                ELSE status
            END;

            UPDATE pricing."RateHeaders"
            SET required_approval = (status = 'PendingApproval');
            """
        );
    }
}
