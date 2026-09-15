# ADR-047: Inbox Retention Strategy

## Status

Accepted

## Context

Inbox rows cannot grow forever, but deleting them too early weakens replay protection.

## Decision

Retain processed inbox rows for a configurable window, initially 90 days. Never blindly delete `Processing` or unresolved `Failed` records.

## Consequences

Storage grows with consumer volume and retention. Cleanup must be batched and observable.

## Alternatives Considered

Immediate deletion after processing was rejected because replay and crash recovery need durable history.