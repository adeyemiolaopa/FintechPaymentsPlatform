# ADR-020 Ledger Transaction Isolation And Concurrency

## Status
Accepted

## Context
Concurrent postings against the same accounts must not lose projection updates or duplicate financial effects.

## Decision
Use PostgreSQL transactions, unique idempotency constraints, and row-level locks on balance projection rows in deterministic account-id order.

## Consequences
Overlapping account postings serialize at the projection boundary. Deadlock risk is reduced by deterministic lock ordering.

## Alternatives Considered
Global locks were rejected because they reduce throughput. Redis locks were rejected because Redis is not the ledger correctness boundary.