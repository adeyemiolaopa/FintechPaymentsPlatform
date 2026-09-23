using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationExceptionWorkflow(ReconciliationDbContext db)
{
    public async Task<ReconciliationException?> ResolveAsync(Guid id, string actor, string reasonCode, string comment, CancellationToken ct)
    {
        var item = await db.Exceptions.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (item is null) return null;
        if (item.Status is ExceptionStatus.Resolved or ExceptionStatus.Ignored) throw new InvalidOperationException("Exception is already terminal.");
        var now = DateTimeOffset.UtcNow;
        item.Status = ExceptionStatus.Resolved;
        item.ResolvedAtUtc = now;
        item.UpdatedAtUtc = now;
        db.AuditEvents.Add(new ReconciliationAuditEvent { ExceptionId = id, Action = "ExceptionResolved", Actor = actor, ReasonCode = reasonCode, Comment = comment });
        db.OutboxMessages.Add(new ReconciliationOutboxMessage
        {
            EventType = "reconciliation.exception.resolved",
            PartitionKey = item.Id.ToString("D"),
            Payload = JsonSerializer.Serialize(new { ReconciliationExceptionId = item.Id, item.Provider, item.PaymentId, ExceptionCode = item.Code.ToString(), Actor = actor, ReasonCode = reasonCode, item.ResolvedAtUtc })
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return item;
    }
}
