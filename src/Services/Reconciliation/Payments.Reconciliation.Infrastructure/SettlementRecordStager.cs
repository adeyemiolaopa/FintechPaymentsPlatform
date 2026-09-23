using Microsoft.EntityFrameworkCore;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public static class SettlementRecordStager
{
    public static async Task StageAsync(ReconciliationDbContext db, ISettlementFileStore store, SettlementFile file, int batchSize, CancellationToken cancellationToken)
    {
        var lastLine = await db.SettlementRecords.AsNoTracking()
            .Where(row => row.SettlementFileId == file.Id)
            .MaxAsync(row => (long?)row.SourceLineNumber, cancellationToken).ConfigureAwait(false) ?? 1;
        if (lastLine >= file.RecordCount + 1) return;

        using var stream = store.OpenRead(file.Provider, file.FileHash);
        var staged = new List<SettlementRecord>(Math.Clamp(batchSize, 1, 1000));
        foreach (var record in SettlementCsvParser.Parse(stream, file.Id))
        {
            if (record.SourceLineNumber <= lastLine) continue;
            db.SettlementRecords.Add(record);
            staged.Add(record);
            if (staged.Count < Math.Clamp(batchSize, 1, 1000)) continue;
            await FlushAsync(db, file, staged, cancellationToken).ConfigureAwait(false);
        }
        if (staged.Count > 0) await FlushAsync(db, file, staged, cancellationToken).ConfigureAwait(false);
    }

    private static async Task FlushAsync(ReconciliationDbContext db, SettlementFile file, List<SettlementRecord> staged, CancellationToken cancellationToken)
    {
        file.LeaseUntilUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in staged) db.Entry(record).State = EntityState.Detached;
        staged.Clear();
    }
}
