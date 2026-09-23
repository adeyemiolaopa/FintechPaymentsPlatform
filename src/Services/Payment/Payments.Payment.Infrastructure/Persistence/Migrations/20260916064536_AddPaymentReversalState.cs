using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReversalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversalLedgerTransactionId",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                schema: "payment",
                table: "payments",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReversedAtUtc",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReversalLedgerTransactionId",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                schema: "payment",
                table: "payments");
        }
    }
}
