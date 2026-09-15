using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRequestIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_idempotency_records",
                schema: "payment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_idempotency_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_idempotency_records_CustomerId_OperationType_Idempo~",
                schema: "payment",
                table: "payment_idempotency_records",
                columns: new[] { "CustomerId", "OperationType", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_idempotency_records_ExpiresAtUtc",
                schema: "payment",
                table: "payment_idempotency_records",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payment_idempotency_records_ResourceId",
                schema: "payment",
                table: "payment_idempotency_records",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_idempotency_records_Status_UpdatedAtUtc",
                schema: "payment",
                table: "payment_idempotency_records",
                columns: new[] { "Status", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_idempotency_records",
                schema: "payment");
        }
    }
}
