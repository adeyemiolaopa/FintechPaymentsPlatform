using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Reconciliation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveReconciliationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "active_reconciliation_jobs",
                schema: "reconciliation",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NextReconciliationAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_active_reconciliation_jobs", x => x.PaymentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_active_reconciliation_jobs_NextReconciliationAtUtc_LeaseUnt~",
                schema: "reconciliation",
                table: "active_reconciliation_jobs",
                columns: new[] { "NextReconciliationAtUtc", "LeaseUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_reconciliation_jobs",
                schema: "reconciliation");
        }
    }
}
