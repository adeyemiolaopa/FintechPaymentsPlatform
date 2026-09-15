# Runbook: Kafka Outbox Backlog

Symptoms: increasing pending rows, old `NextAttemptAtUtc`, or payment/customer/account events not arriving downstream.

Checks:

```sql
SELECT "Topic", "Status", count(*), min("OccurredAtUtc"), max("LastError")
FROM payment.outbox_messages
GROUP BY "Topic", "Status"
ORDER BY "Topic", "Status";
```

Repeat for `identity`, `customer`, `account`, and `ledger` schemas.

Actions:

1. Confirm Kafka health and topic existence with `kafka-topics.sh --list`.
2. Confirm the service outbox publisher is enabled with `Outbox__PublisherEnabled=true`.
3. Inspect recent service logs for delivery exceptions and broker timeouts.
4. Scale service instances if pending rows are high but attempts are succeeding.
5. If rows are `Failed`, follow `failed-outbox-message.md`.