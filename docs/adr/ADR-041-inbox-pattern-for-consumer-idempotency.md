# ADR-041: Inbox Pattern for Consumer Idempotency

## Status

Accepted

## Context

Kafka is at least once. Duplicate delivery, replay, and crashes must not create duplicate business effects.

## Decision

Each consuming bounded context owns a local inbox table keyed by `ConsumerName + EventId`. Business mutation and inbox completion occur in one database transaction where practical.

## Consequences

Redelivery after database commit is safe. Consumers need retention and cleanup policy. Remote side effects still require downstream idempotency.

## Alternatives Considered

Kafka exactly-once semantics alone and Redis-only deduplication were rejected because financial correctness needs durable local state.