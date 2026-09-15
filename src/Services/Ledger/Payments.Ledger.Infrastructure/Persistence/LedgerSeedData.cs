using Microsoft.EntityFrameworkCore;
using Payments.Ledger.Domain.Ledger;

namespace Payments.Ledger.Infrastructure.Persistence;

public static class LedgerSeedData
{
    private static readonly (string ExternalReference, string Code, string Name, LedgerAccountType Type, string Currency)[] DevelopmentAccounts =
    [
        ("system:ngn:settlement_asset", "1100-NGN-SETTLEMENT", "NGN Settlement Asset", LedgerAccountType.Asset, "NGN"),
        ("system:ngn:clearing", "1200-NGN-CLEARING", "NGN Clearing Asset", LedgerAccountType.Asset, "NGN"),
        ("system:ngn:fee_revenue", "4100-NGN-FEE-REVENUE", "NGN Fee Revenue", LedgerAccountType.Revenue, "NGN"),
        ("system:ngn:suspense", "9000-NGN-SUSPENSE", "NGN Suspense Control", LedgerAccountType.Liability, "NGN"),
    ];

    public static async Task SeedAsync(LedgerDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var seed in DevelopmentAccounts)
        {
            if (await dbContext.LedgerAccounts.AnyAsync(account => account.ExternalReference == seed.ExternalReference, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var account = LedgerAccount.Create(seed.ExternalReference, seed.Code, seed.Name, seed.Type, Currency.FromCode(seed.Currency), now);
            dbContext.LedgerAccounts.Add(account);
            dbContext.BalanceProjections.Add(LedgerAccountBalance.Create(account.Id, account.Currency, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
