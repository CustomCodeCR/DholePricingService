using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations;

[DbContext(typeof(ServiceDbContext))]
[Migration("20260923163000_AddMarketAutoPricingDomain")]
public partial class AddMarketAutoPricingDomain : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AutoPricingProfiles",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                target_percentile = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                competitive_ceiling_percentile = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                minimum_confidence_for_auto_apply = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                minimum_confidence_for_suggestion = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                minimum_competitor_count = table.Column<int>(type: "integer", nullable: false),
                minimum_observation_count = table.Column<int>(type: "integer", nullable: false),
                maximum_market_deviation = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AutoPricingProfiles", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "ChargePricingRules",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                charge_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                can_auto_adjust = table.Column<bool>(type: "boolean", nullable: false),
                adjustment_priority = table.Column<int>(type: "integer", nullable: false),
                minimum_markup = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                maximum_markup = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                maximum_adjustment_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                adjustment_strategy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChargePricingRules", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "CompetitorRateObservations",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                competitor_tariff_id = table.Column<Guid>(type: "uuid", nullable: true),
                competitor_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                competitor_company_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                source_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                source_import_id = table.Column<Guid>(type: "uuid", nullable: true),
                incoterm_id = table.Column<Guid>(type: "uuid", nullable: true),
                incoterm_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                pol_id = table.Column<Guid>(type: "uuid", nullable: true),
                pol_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                pol_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                poe_id = table.Column<Guid>(type: "uuid", nullable: true),
                poe_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                poe_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                pod_id = table.Column<Guid>(type: "uuid", nullable: true),
                pod_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                pod_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                carrier_id = table.Column<Guid>(type: "uuid", nullable: true),
                carrier_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                carrier_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                container_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                container_type_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                quantity = table.Column<int>(type: "integer", nullable: false),
                mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                normalized_currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                original_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                normalized_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                exchange_rate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                exchange_rate_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ocean_freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                origin_charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                destination_charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                inland_charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                other_charges = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                normalized_ocean_freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                normalized_all_in = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                rate_basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                extraction_confidence = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                normalization_confidence = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                raw_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                imported_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompetitorRateObservations", x => x.id);
                table.ForeignKey(
                    name: "FK_CompetitorRateObservations_CompetitorTariffs",
                    column: x => x.competitor_tariff_id,
                    principalSchema: "pricing",
                    principalTable: "CompetitorTariffs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "PricingMarketDecisions",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                rate_id = table.Column<Guid>(type: "uuid", nullable: false),
                calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                comparison_incoterm_id = table.Column<Guid>(type: "uuid", nullable: true),
                comparison_pol_id = table.Column<Guid>(type: "uuid", nullable: false),
                comparison_poe_id = table.Column<Guid>(type: "uuid", nullable: true),
                comparison_pod_id = table.Column<Guid>(type: "uuid", nullable: true),
                comparison_container_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                comparison_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                comparison_carrier_id = table.Column<Guid>(type: "uuid", nullable: true),
                cost_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                original_sale_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                suggested_sale_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                final_sale_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                average = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                weighted_average = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                median = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p25 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p40 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p50 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p60 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p65 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                p75 = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                target_market_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                competitive_ceiling = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                competitor_count = table.Column<int>(type: "integer", nullable: false),
                observation_count = table.Column<int>(type: "integer", nullable: false),
                confidence_score = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                algorithm_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                was_auto_applied = table.Column<bool>(type: "boolean", nullable: false),
                was_manually_modified = table.Column<bool>(type: "boolean", nullable: false),
                reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PricingMarketDecisions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "PricingMarketDecisionObservations",
            schema: "pricing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                pricing_market_decision_id = table.Column<Guid>(type: "uuid", nullable: false),
                competitor_rate_observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                competitor_company_id = table.Column<Guid>(type: "uuid", nullable: true),
                original_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                normalized_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                comparability_score = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                recency_weight = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                final_weight = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                is_outlier = table.Column<bool>(type: "boolean", nullable: false),
                was_included = table.Column<bool>(type: "boolean", nullable: false),
                exclusion_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PricingMarketDecisionObservations", x => x.id);
                table.ForeignKey(
                    name: "FK_PricingMarketDecisionObservations_Decisions",
                    column: x => x.pricing_market_decision_id,
                    principalSchema: "pricing",
                    principalTable: "PricingMarketDecisions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PricingMarketDecisionObservations_Observations",
                    column: x => x.competitor_rate_observation_id,
                    principalSchema: "pricing",
                    principalTable: "CompetitorRateObservations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "UX_AutoPricingProfiles_Code",
            schema: "pricing",
            table: "AutoPricingProfiles",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AutoPricingProfiles_IsActive",
            schema: "pricing",
            table: "AutoPricingProfiles",
            column: "is_active");

        migrationBuilder.CreateIndex(
            name: "UX_ChargePricingRules_ChargeCode",
            schema: "pricing",
            table: "ChargePricingRules",
            column: "charge_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ChargePricingRules_Active_Priority",
            schema: "pricing",
            table: "ChargePricingRules",
            columns: new[] { "is_active", "adjustment_priority" });

        migrationBuilder.CreateIndex(
            name: "IX_CompetitorRateObservations_CompetitorTariffId",
            schema: "pricing",
            table: "CompetitorRateObservations",
            column: "competitor_tariff_id");

        migrationBuilder.CreateIndex(
            name: "IX_CompetitorRateObservations_CompetitorCompanyId",
            schema: "pricing",
            table: "CompetitorRateObservations",
            column: "competitor_company_id");

        migrationBuilder.CreateIndex(
            name: "IX_CompetitorRateObservations_MarketKey",
            schema: "pricing",
            table: "CompetitorRateObservations",
            columns: new[] { "mode", "incoterm_id", "pol_id", "poe_id", "pod_id", "container_type_id", "carrier_id" });

        migrationBuilder.CreateIndex(
            name: "IX_CompetitorRateObservations_Validity",
            schema: "pricing",
            table: "CompetitorRateObservations",
            columns: new[] { "valid_from", "valid_to" });

        migrationBuilder.CreateIndex(
            name: "IX_PricingMarketDecisions_Rate_CalculatedAt",
            schema: "pricing",
            table: "PricingMarketDecisions",
            columns: new[] { "rate_id", "calculated_at_utc" });

        migrationBuilder.CreateIndex(
            name: "IX_PricingMarketDecisions_MarketKey",
            schema: "pricing",
            table: "PricingMarketDecisions",
            columns: new[] { "comparison_mode", "comparison_incoterm_id", "comparison_pol_id", "comparison_poe_id", "comparison_pod_id", "comparison_container_type_id", "comparison_carrier_id" });

        migrationBuilder.CreateIndex(
            name: "UX_PricingMarketDecisionObservations_Decision_Observation",
            schema: "pricing",
            table: "PricingMarketDecisionObservations",
            columns: new[] { "pricing_market_decision_id", "competitor_rate_observation_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PricingMarketDecisionObservations_ObservationId",
            schema: "pricing",
            table: "PricingMarketDecisionObservations",
            column: "competitor_rate_observation_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PricingMarketDecisionObservations",
            schema: "pricing");

        migrationBuilder.DropTable(
            name: "AutoPricingProfiles",
            schema: "pricing");

        migrationBuilder.DropTable(
            name: "ChargePricingRules",
            schema: "pricing");

        migrationBuilder.DropTable(
            name: "PricingMarketDecisions",
            schema: "pricing");

        migrationBuilder.DropTable(
            name: "CompetitorRateObservations",
            schema: "pricing");
    }
}
