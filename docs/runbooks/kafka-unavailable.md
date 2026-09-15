# Runbook: Kafka Unavailable

If Kafka is unavailable, API writes can continue because business data and outbox rows are committed in PostgreSQL. Event delivery pauses until the broker recovers.

Actions:

1. Check broker/container status and disk pressure.
2. Check `kafka-topic-bootstrap` status after restart when auto topic creation is disabled.
3. Keep API services running unless database pressure requires throttling.
4. Monitor outbox pending counts and oldest `OccurredAtUtc`.
5. After Kafka recovers, publishers will drain pending rows with retry backoff.

Risk: downstream services may observe delayed state. Do not manually emit replacement events while original outbox rows remain pending.