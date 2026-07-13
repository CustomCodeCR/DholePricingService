using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dhole.Pricing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.CreateTable(
                name: "Costs",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_detail_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    carrier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carrier_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    carrier_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    agent_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    port_id = table.Column<Guid>(type: "uuid", nullable: false),
                    port_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    port_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    port_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    utility_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_costs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ImportFclRates",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extraction_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    import_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_profile_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    import_profile_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    import_profile_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pol_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    pol_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pol_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    poe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poe = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    poe_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    poe_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    poe_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pod_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pod = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pod_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    pod_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pod_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    carrier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    carrier_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    agent_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    container_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    container_type_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    container_type_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    container_type_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency_slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    commodity = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ocean_freight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    origin_charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    destination_charges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    surcharges = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    total_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    total_sale = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    profit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    margin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    free_days = table.Column<int>(type: "integer", nullable: false),
                    transit_days = table.Column<int>(type: "integer", nullable: true),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    raw_data_json = table.Column<string>(type: "jsonb", nullable: true),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    used_as_rate_count = table.Column<int>(type: "integer", nullable: false),
                    created_as_rate_header_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_import_fcl_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consumer_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_service = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    headers_json = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RateHeaders",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_import_fcl_rate_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    agent_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    carrier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    carrier_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    carrier_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    pol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pol_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    pol_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    poe_id = table.Column<Guid>(type: "uuid", nullable: false),
                    poe_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    poe_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    pod_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pod_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    pod_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    container_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_type_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    container_type_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    free_days = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_utility_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    margin_percentage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    required_approval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_rate_headers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RateDetails",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_header_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    cost_detail_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cost_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sale_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    utility_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_rate_details", x => x.id);
                    table.ForeignKey(
                        name: "f_k_rate_details__rate_headers_rate_header_id",
                        column: x => x.rate_header_id,
                        principalSchema: "pricing",
                        principalTable: "RateHeaders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Costs_agent_id",
                schema: "pricing",
                table: "Costs",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_carrier_id",
                schema: "pricing",
                table: "Costs",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_cost_detail_type",
                schema: "pricing",
                table: "Costs",
                column: "cost_detail_type");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_cost_type",
                schema: "pricing",
                table: "Costs",
                column: "cost_type");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_currency_id",
                schema: "pricing",
                table: "Costs",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_is_active",
                schema: "pricing",
                table: "Costs",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_name",
                schema: "pricing",
                table: "Costs",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_port_id",
                schema: "pricing",
                table: "Costs",
                column: "port_id");

            migrationBuilder.CreateIndex(
                name: "IX_Costs_port_role",
                schema: "pricing",
                table: "Costs",
                column: "port_role");

            migrationBuilder.CreateIndex(
                name: "ix_costs_template_unique",
                schema: "pricing",
                table: "Costs",
                columns: new[] { "cost_type", "cost_detail_type", "carrier_id", "agent_id", "port_id", "port_role", "name", "currency_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "IX_ImportFclRates_carrier_id_pol_id_poe_id_pod_id_container_ty~",
                schema: "pricing",
                table: "ImportFclRates",
                columns: new[] { "carrier_id", "pol_id", "poe_id", "pod_id", "container_type_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportFclRates_extraction_record_id",
                schema: "pricing",
                table: "ImportFclRates",
                column: "extraction_record_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportFclRates_import_batch_id",
                schema: "pricing",
                table: "ImportFclRates",
                column: "import_batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_ImportFclRates_status",
                schema: "pricing",
                table: "ImportFclRates",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_event_id_consumer_service",
                schema: "pricing",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_service" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_status_created_at",
                schema: "pricing",
                table: "inbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "pricing",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_status_created_at",
                schema: "pricing",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_rate_details_rate_header_id",
                schema: "pricing",
                table: "RateDetails",
                column: "rate_header_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_details_lookup",
                schema: "pricing",
                table: "RateDetails",
                columns: new[] { "rate_header_id", "cost_id", "name", "cost_detail_type", "cost_type", "currency_id" });

            migrationBuilder.CreateIndex(
                name: "IX_RateDetails_cost_detail_type",
                schema: "pricing",
                table: "RateDetails",
                column: "cost_detail_type");

            migrationBuilder.CreateIndex(
                name: "IX_RateDetails_cost_id",
                schema: "pricing",
                table: "RateDetails",
                column: "cost_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateDetails_cost_type",
                schema: "pricing",
                table: "RateDetails",
                column: "cost_type");

            migrationBuilder.CreateIndex(
                name: "IX_RateDetails_currency_id",
                schema: "pricing",
                table: "RateDetails",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_rate_headers_source_import_fcl_rate_id_unique",
                schema: "pricing",
                table: "RateHeaders",
                column: "source_import_fcl_rate_id",
                unique: true,
                filter: "source_import_fcl_rate_id IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_rate_headers_valid_lookup",
                schema: "pricing",
                table: "RateHeaders",
                columns: new[] { "agent_id", "carrier_id", "pol_id", "poe_id", "pod_id", "container_type_id", "currency_id", "status", "valid_from", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_agent_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_carrier_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "carrier_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_container_type_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "container_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_currency_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_pod_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "pod_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_poe_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "poe_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_pol_id",
                schema: "pricing",
                table: "RateHeaders",
                column: "pol_id");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_required_approval",
                schema: "pricing",
                table: "RateHeaders",
                column: "required_approval");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_status",
                schema: "pricing",
                table: "RateHeaders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_valid_from",
                schema: "pricing",
                table: "RateHeaders",
                column: "valid_from");

            migrationBuilder.CreateIndex(
                name: "IX_RateHeaders_valid_to",
                schema: "pricing",
                table: "RateHeaders",
                column: "valid_to");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Costs",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "ImportFclRates",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "RateDetails",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "RateHeaders",
                schema: "pricing");
        }
    }
}
