using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalRailRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rail_callback_inbox",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CallbackEventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClientReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rail_callback_inbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rail_submission_attempts",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RailSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ClientReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RawStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rail_submission_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rail_submissions",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Market = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ClientReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InstructionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RespondedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResponseCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RawStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastStatusCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextStatusCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusCheckCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rail_submissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rail_callback_inbox_PaymentId",
                schema: "payment",
                table: "rail_callback_inbox",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_rail_callback_inbox_Provider_CallbackEventId",
                schema: "payment",
                table: "rail_callback_inbox",
                columns: new[] { "Provider", "CallbackEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rail_callback_inbox_ProviderReference",
                schema: "payment",
                table: "rail_callback_inbox",
                column: "ProviderReference");

            migrationBuilder.CreateIndex(
                name: "IX_rail_submission_attempts_PaymentId_StartedAtUtc",
                schema: "payment",
                table: "rail_submission_attempts",
                columns: new[] { "PaymentId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_rail_submission_attempts_RailSubmissionId_AttemptNumber",
                schema: "payment",
                table: "rail_submission_attempts",
                columns: new[] { "RailSubmissionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rail_submissions_PaymentId",
                schema: "payment",
                table: "rail_submissions",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rail_submissions_Provider_ClientReference",
                schema: "payment",
                table: "rail_submissions",
                columns: new[] { "Provider", "ClientReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rail_submissions_Provider_ProviderReference",
                schema: "payment",
                table: "rail_submissions",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_rail_submissions_Status_NextStatusCheckAtUtc",
                schema: "payment",
                table: "rail_submissions",
                columns: new[] { "Status", "NextStatusCheckAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rail_callback_inbox",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "rail_submission_attempts",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "rail_submissions",
                schema: "payment");
        }
    }
}
