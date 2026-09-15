# ADR-027 Payment Kafka Partition Strategy

Status: Accepted

Payment lifecycle events are published to `payments.lifecycle.v1` through transactional outbox. The partition key is PaymentId to preserve per-payment event ordering without exposing unnecessary financial details.
