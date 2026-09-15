# Kafka Producer Architecture

Kafka is used for integration events between bounded contexts. Producers do not publish directly from controllers or domain code. Service infrastructure writes outbox rows and background publishers own broker delivery.

Producer durability settings are centralized in `KafkaProducerConfigFactory`:

- `EnableIdempotence = true`
- `Acks = All`
- bounded request and message timeouts
- small linger for batching
- compression enabled by default

Docker Compose disables broker auto topic creation and runs a one-shot bootstrap service. This catches topic/catalog drift during local development and mirrors production operational practice.