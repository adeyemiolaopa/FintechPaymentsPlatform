# ADR-032 Idempotency Retention Strategy

## Status

Accepted

## Context

Financial clients may retry after timeouts, app restarts, or delayed network recovery. Retention that is too short risks duplicate payments. Retaining full response bodies forever is unnecessary.

## Decision

The default retention window is 7 days and is configurable with `PaymentIdempotency:RetentionDays`. Cleanup prunes expired completed response snapshots but keeps key/hash/resource identity longer.

## Consequences

The platform favors duplicate protection over aggressive key reuse. Cleanup does not delete active `Processing` records and uses bounded PostgreSQL batches with `FOR UPDATE SKIP LOCKED`.

## Alternatives Considered

Deleting all expired records was rejected because old retries could create new payments. Keeping response payloads indefinitely was rejected because it increases storage and financial data exposure.