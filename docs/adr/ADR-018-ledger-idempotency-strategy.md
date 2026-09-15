# ADR-018 Ledger Idempotency Strategy

## Status
Accepted

## Context
Clients may retry after timeouts or connection loss, including after the database committed.

## Decision
Use `ExternalReference` as the idempotency key and store a canonical SHA-256 hash of financially relevant request fields. The database enforces uniqueness.

## Consequences
Same reference and same instruction returns the existing transaction. Same reference and different instruction conflicts.

## Alternatives Considered
Application-only lookup was rejected because it has race conditions under concurrent submissions.