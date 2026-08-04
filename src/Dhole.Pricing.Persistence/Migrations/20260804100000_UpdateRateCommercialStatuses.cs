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
