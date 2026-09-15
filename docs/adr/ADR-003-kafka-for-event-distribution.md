# ADR-003: Kafka for Event Distribution

## Status

Accepted

## Context

The platform needs high-throughput asynchronous workflows, replayable integration contracts, and future reconciliation flows.

## Decision

Use Kafka behind an `IEventPublisher` abstraction. Domain events remain internal and are not serialized directly to topics.

## Consequences

Kafka supports scale and durable event distribution. It introduces operational complexity, schema compatibility concerns, and the risk of retry amplification if consumers are careless.

## Alternatives Considered

RabbitMQ is simpler for command-style messaging but weaker for log-based stream processing. Direct HTTP callbacks were rejected for resilience and replay limitations.