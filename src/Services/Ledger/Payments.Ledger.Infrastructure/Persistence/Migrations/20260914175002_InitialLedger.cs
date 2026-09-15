using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "ledger_account_balances",
                schema: "ledger",
                columns: table => new
                {
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DebitTotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CreditTotal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_account_balances", x => x.LedgerAccountId);
                    table.CheckConstraint("ck_ledger_balances_credit_non_negative", "\"CreditTotal\" >= 0");
                    table.CheckConstraint("ck_ledger_balances_debit_non_negative", "\"DebitTotal\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_audit_events",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_idempotency_records",
                schema: "ledger",
                columns: table => new
                {
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_idempotency_records", x => x.IdempotencyKey);
                });

            migrationBuilder.CreateTable(
                name: "ledger_reversals",
                schema: "ledger",
                columns: table => new
                {
                    OriginalTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReversalTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ReversedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_reversals", x => x.OriginalTransactionId);
                });

            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CausationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OriginalTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversalReason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "ledger",
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
                name: "processed_integration_events",
                schema: "ledger",
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

            migrationBuilder.CreateTable(
                name: "ledger_postings",
                schema: "ledger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_postings", x => x.Id);
                    table.CheckConstraint("ck_ledger_postings_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ledger_postings_ledger_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalSchema: "ledger",
                        principalTable: "ledger_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_AccountCode",
                schema: "ledger",
                table: "ledger_accounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_Currency_AccountType",
                schema: "ledger",
                table: "ledger_accounts",
                columns: new[] { "Currency", "AccountType" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_accounts_ExternalReference",
                schema: "ledger",
                table: "ledger_accounts",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_audit_events_LedgerAccountId",
                schema: "ledger",
                table: "ledger_audit_events",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_audit_events_OccurredAtUtc",
                schema: "ledger",
                table: "ledger_audit_events",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_audit_events_TransactionId",
                schema: "ledger",
                table: "ledger_audit_events",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_idempotency_records_TransactionId",
                schema: "ledger",
                table: "ledger_idempotency_records",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_postings_LedgerAccountId_CreatedAtUtc_Id",
                schema: "ledger",
                table: "ledger_postings",
                columns: new[] { "LedgerAccountId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ledger_postings_TransactionId",
                schema: "ledger",
                table: "ledger_postings",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_postings_TransactionId_Sequence",
                schema: "ledger",
                table: "ledger_postings",
                columns: new[] { "TransactionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_reversals_ReversalTransactionId",
                schema: "ledger",
                table: "ledger_reversals",
                column: "ReversalTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_transactions_CorrelationId",
                schema: "ledger",
                table: "ledger_transactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_transactions_ExternalReference",
                schema: "ledger",
                table: "ledger_transactions",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ledger_transactions_OccurredAtUtc",
                schema: "ledger",
                table: "ledger_transactions",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAtUtc",
                schema: "ledger",
                table: "outbox_messages",
                column: "PublishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_processed_integration_events_EventType",
                schema: "ledger",
                table: "processed_integration_events",
                column: "EventType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_account_balances",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_audit_events",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_idempotency_records",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_postings",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_reversals",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_transactions",
                schema: "ledger");
        }
    }
}
