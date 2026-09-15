# Runbook: Kafka Poison Message

A poison message repeatedly fails for deterministic reasons such as malformed JSON, unsupported event version, or invalid required data.

Steps:

1. Identify `EventId`, topic, partition, offset, consumer group, and failure category.
2. Inspect sanitized payload and schema version.
3. Confirm whether the failure is deterministic or transient.
4. Fix consumer code, schema compatibility, or source data.
5. Replay from DLQ using the same `EventId`.
6. Verify inbox status and business side effects.

Do not edit payloads in place without a data repair review.