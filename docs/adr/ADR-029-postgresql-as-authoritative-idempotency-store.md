# ADR-029 PostgreSQL As Authoritative Idempotency Store

## Status

Accepted

## Context

Idempotency must hold across processes, service instances, restarts, cache outages, and concurrent duplicate requests.

## Decision

PostgreSQL is the authoritative idempotency store. The final correctness guarantee is the unique constraint on `(CustomerId, OperationType, IdempotencyKey)` in `payment.payment_idempotency_records`.

## Consequences

No in-memory lock or Redis lock is required for correctness. Concurrent requests race at the database constraint. Redis may later cache completed lookups, but a Redis failure cannot permit duplicate payments.

## Alternatives Considered

`lock`, `SemaphoreSlim`, and static dictionaries were rejected because they only protect one process. Redis `SETNX` was rejected as authoritative storage because failover, eviction, or TTL mistakes can re-enable duplicates.