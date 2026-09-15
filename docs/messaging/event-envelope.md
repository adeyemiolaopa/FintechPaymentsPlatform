# Event Envelope

Integration events are serialized as `IntegrationEventEnvelope<TPayload>`.

Required metadata:

- `eventId`: globally unique event identity used for tracing and future consumer deduplication.
- `eventType`: stable semantic contract name.
- `eventVersion`: integer schema version for the payload shape.
- `occurredAtUtc`: event creation time in UTC.
- `correlationId`: request or workflow correlation id.
- `producer`: service name.
- `payload`: event-specific data.

Optional metadata:

- `causationId`: upstream event or command id.
- `aggregateId`: aggregate that produced the event.
- `partitionKey`: routing key used as the Kafka message key.

Kafka headers repeat the delivery metadata: `event-id`, `event-type`, `event-version`, `correlation-id`, `producer`, `occurred-at`, `content-type`, plus optional `causation-id`, `aggregate-id`, and `partition-key`. Consumers must treat headers as routing/observability metadata and the JSON envelope as the durable contract.