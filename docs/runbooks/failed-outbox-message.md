# Runbook: Failed Outbox Message

A row becomes `Failed` after the configured maximum attempts. Failed rows are not automatically retried until an operator resets them.

Inspect:

```sql
SELECT "Id", "EventId", "EventType", "Topic", "PartitionKey", "AttemptCount", "LastError"
FROM payment.outbox_messages
WHERE "Status" = 'Failed'
ORDER BY "LastAttemptAtUtc" DESC;
```

After fixing the root cause, reset a single row:

```sql
UPDATE payment.outbox_messages
SET "Status" = 'Pending',
    "NextAttemptAtUtc" = now(),
    "LastError" = NULL
WHERE "Id" = '<outbox-id>' AND "Status" = 'Failed';
```

Do not edit `Payload`, `EventId`, or `PartitionKey` without a data repair review.