# Consumer Idempotency

Deduplication uses `EventId`, not Kafka topic/partition/offset. Offsets are broker positions and can change during replay or republishing; `EventId` is the logical event identity.

The unique key is:

```text
ConsumerName + EventId
```

This lets two different consumers process the same event independently, while preventing duplicate business effects inside one logical consumer.

The inbox stores a `PayloadHash`. If the same `ConsumerName + EventId` appears with a different payload hash, the consumer treats it as an integrity violation, marks the inbox row failed, and sends the message to the consumer DLQ.