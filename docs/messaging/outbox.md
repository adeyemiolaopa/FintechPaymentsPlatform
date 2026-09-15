# Transactional Outbox

Each service writes business state and an outbox row in the same PostgreSQL transaction. Background publishers deliver pending rows to Kafka with idempotent producer settings and mark successful rows as `Published`.

Standard columns include `EventId`, `EventType`, `EventVersion`, `AggregateType`, `AggregateId`, `Topic`, `Key`, `PartitionKey`, `Payload`, `Headers`, `Status`, timestamps, attempt counters, retry time, and last error.

Processing behavior:

- Workers select `Pending` rows due for delivery with `FOR UPDATE SKIP LOCKED`.
- Multiple instances can publish concurrently without taking the same row.
- Failed sends retry with exponential backoff and jitter.
- Rows move to `Failed` after `MaxAttempts`; operators can inspect and manually reset them.
- Published rows are cleaned in bounded batches after `PublishedRetentionDays`.

Delivery is at least once. Consumers must be idempotent; consumer inbox/deduplication is scheduled for Week 8.