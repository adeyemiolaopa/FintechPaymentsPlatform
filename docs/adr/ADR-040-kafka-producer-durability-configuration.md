# ADR-040: Kafka Producer Durability Configuration

## Status

Accepted

## Context

Producer defaults can acknowledge messages before replicas have accepted them or allow duplicate retries.

## Decision

Centralize producer settings with idempotence enabled, `acks=all`, bounded request/message timeouts, linger, and compression.

## Consequences

Producer delivery favors reliability over the lowest possible latency. Services share one configuration factory and tests pin the expected settings.