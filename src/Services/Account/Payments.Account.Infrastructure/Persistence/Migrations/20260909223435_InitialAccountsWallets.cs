using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Account.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountsWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "account");

            migrationBuilder.CreateTable(
                name: "account_audit_events",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "account_limits",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LimitType = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_limits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "account_restrictions",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestrictionType = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_restrictions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LedgerBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    ReservedBalance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                    table.CheckConstraint("ck_accounts_available_non_negative", "\"LedgerBalance\" - \"ReservedBalance\" >= 0");
                    table.CheckConstraint("ck_accounts_ledger_non_negative", "\"LedgerBalance\" >= 0");
                    table.CheckConstraint("ck_accounts_reserved_non_negative", "\"ReservedBalance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "beneficiaries",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BankCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beneficiaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_references",
                schema: "account",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    KycStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_references", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "funds_reservations",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CommittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_funds_reservations", x => x.Id);
                    table.CheckConstraint("ck_funds_reservations_amount_positive", "\"Amount\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "account",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "account",
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
                name: "IX_account_audit_events_AccountId",
                schema: "account",
                table: "account_audit_events",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_account_audit_events_CustomerId",
                schema: "account",
                table: "account_audit_events",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_audit_events_OccurredAtUtc",
                schema: "account",
                table: "account_audit_events",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_account_limits_AccountId_LimitType",
                schema: "account",
                table: "account_limits",
                columns: new[] { "AccountId", "LimitType" });

            migrationBuilder.CreateIndex(
                name: "IX_account_restrictions_AccountId_RestrictionType",
                schema: "account",
                table: "account_restrictions",
                columns: new[] { "AccountId", "RestrictionType" },
                filter: "\"RemovedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_AccountNumber",
                schema: "account",
                table: "accounts",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_CustomerId_Currency_AccountType",
                schema: "account",
                table: "accounts",
                columns: new[] { "CustomerId", "Currency", "AccountType" },
                unique: true,
                filter: "\"Status\" <> 'Closed'");

            migrationBuilder.CreateIndex(
                name: "IX_beneficiaries_CustomerId_BankCode_AccountNumber_Currency",
                schema: "account",
                table: "beneficiaries",
                columns: new[] { "CustomerId", "BankCode", "AccountNumber", "Currency" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_funds_reservations_AccountId_ReferenceId",
                schema: "account",
                table: "funds_reservations",
                columns: new[] { "AccountId", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_funds_reservations_Status",
                schema: "account",
                table: "funds_reservations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_PublishedAtUtc",
                schema: "account",
                table: "outbox_messages",
                column: "PublishedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_audit_events",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_limits",
                schema: "account");

            migrationBuilder.DropTable(
                name: "account_restrictions",
                schema: "account");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "account");

            migrationBuilder.DropTable(
                name: "beneficiaries",
                schema: "account");

            migrationBuilder.DropTable(
                name: "customer_references",
                schema: "account");

            migrationBuilder.DropTable(
                name: "funds_reservations",
                schema: "account");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "account");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "account");
        }
    }
}
