using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Customer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCustomerLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customer");

            migrationBuilder.CreateTable(
                name: "customer_audit_events",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetCustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MiddleName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KycStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    address_city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    address_region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    address_country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    address_postal_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "customer",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_audit_events_OccurredAtUtc",
                schema: "customer",
                table: "customer_audit_events",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_customer_audit_events_TargetCustomerId",
                schema: "customer",
                table: "customer_audit_events",
                column: "TargetCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_Email",
                schema: "customer",
                table: "customers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_customers_IdentityUserId",
                schema: "customer",
                table: "customers",
                column: "IdentityUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_PhoneNumber",
                schema: "customer",
                table: "customers",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_EventType",
                schema: "customer",
                table: "processed_integration_events",
                column: "EventType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_audit_events",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "customer");
        }
    }
}
