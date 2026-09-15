using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPaymentOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.CreateTable(
                name: "account_references",
                schema: "payment",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_references", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "customer_references",
                schema: "payment",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    KycStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_references", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_audit_events",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_state_transitions",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReasonDescription = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_state_transitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationBankCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    DestinationAccountNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DestinationAccountName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DestinationCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PaymentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FundsReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReasonDescription = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.CheckConstraint("ck_payments_amount_positive", "\"Amount\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "payment",
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
                name: "IX_account_references_CustomerId",
                schema: "payment",
                table: "account_references",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_references_LedgerAccountId",
                schema: "payment",
                table: "account_references",
                column: "LedgerAccountId",
                unique: true,
                filter: "\"LedgerAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAtUtc",
                schema: "payment",
                table: "outbox_messages",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_audit_events_PaymentId_OccurredAtUtc",
                schema: "payment",
                table: "payment_audit_events",
                columns: new[] { "PaymentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_state_transitions_PaymentId_OccurredAtUtc",
                schema: "payment",
                table: "payment_state_transitions",
                columns: new[] { "PaymentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_CreatedAtUtc",
                schema: "payment",
                table: "payments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payments_CustomerId_CreatedAtUtc",
                schema: "payment",
                table: "payments",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_payments_ExternalReference",
                schema: "payment",
                table: "payments",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_Reference",
                schema: "payment",
                table: "payments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_Status",
                schema: "payment",
                table: "payments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_payments_Status_UpdatedAtUtc",
                schema: "payment",
                table: "payments",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_EventType",
                schema: "payment",
                table: "processed_integration_events",
                column: "EventType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_references",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "customer_references",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_audit_events",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_state_transitions",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "payment");
        }
    }
}
