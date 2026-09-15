# ADR-036: At-Least-Once Event Delivery

## Status

Accepted

## Context

The platform cannot use a distributed transaction between PostgreSQL and Kafka.

## Decision

Use at-least-once delivery from a transactional outbox. Producers persist outbox rows with business data and publish asynchronously. Consumers must be idempotent; inbox-based deduplication is planned for Week 8.

## Consequences

Events can be delayed or duplicated, but committed business changes are not lost when Kafka is unavailable.