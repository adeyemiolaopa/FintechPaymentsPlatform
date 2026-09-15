# ADR-045: Consumer Group Strategy

## Status

Accepted

## Context

Kafka distributes partitions inside a group; different groups receive streams independently.

## Decision

Each logical business consumer uses a versioned group id. Unrelated consumers do not share groups.

## Consequences

Slow analytics-style consumers do not block financial consumers. Partition count bounds active parallelism per group.

## Alternatives Considered

Sharing groups between unrelated business consumers was rejected.