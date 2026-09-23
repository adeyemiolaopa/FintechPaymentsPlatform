using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Reconciliation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reconciliation");

            migrationBuilder.CreateTable(
                name: "provider_status_observations",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ObservedStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResponseCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_status_observations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_audit_events",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExceptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DownstreamCommand = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_exceptions",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_exceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_matches",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LedgerTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExceptionCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolutionType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_runs",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SettlementFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecordsProcessed = table.Column<long>(type: "bigint", nullable: false),
                    Matched = table.Column<long>(type: "bigint", nullable: false),
                    Exceptions = table.Column<long>(type: "bigint", nullable: false),
                    Resolved = table.Column<long>(type: "bigint", nullable: false),
                    Failed = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settlement_files",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LastProcessedLine = table.Column<long>(type: "bigint", nullable: false),
                    RecordCount = table.Column<long>(type: "bigint", nullable: false),
                    ProcessedCount = table.Column<long>(type: "bigint", nullable: false),
                    MatchedCount = table.Column<long>(type: "bigint", nullable: false),
                    ExceptionCount = table.Column<long>(type: "bigint", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settlement_records",
                schema: "reconciliation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceLineNumber = table.Column<long>(type: "bigint", nullable: false),
                    RawRecordHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_status_observations_PaymentId_ObservedAtUtc",
                schema: "reconciliation",
                table: "provider_status_observations",
                columns: new[] { "PaymentId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_audit_events_ExceptionId_OccurredAtUtc",
                schema: "reconciliation",
                table: "reconciliation_audit_events",
                columns: new[] { "ExceptionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_exceptions_PaymentId_Code",
                schema: "reconciliation",
                table: "reconciliation_exceptions",
                columns: new[] { "PaymentId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_exceptions_Status_Severity_CreatedAtUtc",
                schema: "reconciliation",
                table: "reconciliation_exceptions",
                columns: new[] { "Status", "Severity", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_matches_PaymentId",
                schema: "reconciliation",
                table: "reconciliation_matches",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_matches_ProviderReference",
                schema: "reconciliation",
                table: "reconciliation_matches",
                column: "ProviderReference");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_matches_SettlementRecordId",
                schema: "reconciliation",
                table: "reconciliation_matches",
                column: "SettlementRecordId",
                unique: true,
                filter: "\"SettlementRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_reconciliation_runs_SettlementFileId",
                schema: "reconciliation",
                table: "reconciliation_runs",
                column: "SettlementFileId",
                unique: true,
                filter: "\"SettlementFileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_files_Provider_FileHash",
                schema: "reconciliation",
                table: "settlement_files",
                columns: new[] { "Provider", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_settlement_files_Provider_SettlementDate",
                schema: "reconciliation",
                table: "settlement_files",
                columns: new[] { "Provider", "SettlementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_records_ClientReference",
                schema: "reconciliation",
                table: "settlement_records",
                column: "ClientReference");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_records_ProviderReference",
                schema: "reconciliation",
                table: "settlement_records",
                column: "ProviderReference");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_records_SettlementFileId_SourceLineNumber",
                schema: "reconciliation",
                table: "settlement_records",
                columns: new[] { "SettlementFileId", "SourceLineNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_status_observations",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "reconciliation_audit_events",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "reconciliation_exceptions",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "reconciliation_matches",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "reconciliation_runs",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "settlement_files",
                schema: "reconciliation");

            migrationBuilder.DropTable(
                name: "settlement_records",
                schema: "reconciliation");
        }
    }
}
