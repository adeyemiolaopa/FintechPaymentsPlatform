using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payments.Reconciliation.Infrastructure.Migrations;

[DbContext(typeof(ReconciliationDbContext))]
[Migration("20260918054000_ActiveExceptionUniqueness")]
public sealed class ActiveExceptionUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX ux_reconciliation_active_exception
            ON reconciliation.reconciliation_exceptions
            ("Provider", COALESCE("PaymentId"::text, ''), COALESCE("ProviderReference", ''), "Code")
            WHERE "Status" IN ('Open', 'Investigating');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX reconciliation.ux_reconciliation_active_exception;");
    }
}
