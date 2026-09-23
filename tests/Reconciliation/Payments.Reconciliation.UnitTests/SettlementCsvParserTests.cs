using System.Text;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.UnitTests;

public sealed class SettlementCsvParserTests
{
    [Fact]
    public void Parses_simulator_csv_using_decimal_and_business_date()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("provider_reference,client_reference,amount,currency,status,settlement_date\nRAIL-001,PAY-001,50000.00,NGN,SUCCESS,2026-09-17\n"));
        var row = Assert.Single(SettlementCsvParser.Parse(input, Guid.NewGuid()));
        Assert.Equal(50000m, row.Amount);
        Assert.Equal(new DateOnly(2026, 9, 17), row.SettlementDate);
        Assert.Equal(2, row.SourceLineNumber);
    }

    [Fact]
    public void Rejects_bad_middle_row_instead_of_ignoring_suffix()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("provider_reference,client_reference,amount,currency,status,settlement_date\nRAIL-001,PAY-001,50000.00,NGN,SUCCESS,2026-09-17\nRAIL-002,PAY-002,not-money,NGN,SUCCESS,2026-09-17\nRAIL-003,PAY-003,1,NGN,SUCCESS,2026-09-17\n"));
        Assert.Throws<InvalidDataException>(() => SettlementCsvParser.Parse(input, Guid.NewGuid()).ToList());
    }
}
