# Runbook: Kafka DLQ Replay

Replay requires privileged approval and an audit reason.

Process:

1. Dry-run the EventIds and target topic.
2. Confirm the replay preserves `EventId`.
3. Replay in small batches.
4. Watch consumer errors, retry rate, DLQ rate, lag, and oldest event age.
5. Stop if new DLQ messages appear for the same root cause.
6. Record operator, reason, source DLQ, target topic, EventIds, and timestamps.

A replay that reaches an already processed inbox row should skip business logic and commit safely.