# ADR-012 Account Concurrency Strategy

## Status
Accepted

## Context
Concurrent reservation requests can overspend an account if they read the same available balance and then update independently.

## Decision
Use PostgreSQL row-level locking on the account row for balance-changing reservation and release operations. Keep `Account.Version` as an EF concurrency token for non-locking tracked updates.

## Consequences
Overspend protection is enforced by the database transaction boundary. Throughput is serialized per account, which is acceptable because financial correctness matters more than parallel writes to the same balance. Cross-account operations remain independent.

## Alternatives Considered
Optimistic retries were considered, but retry loops are harder to reason about for reservation idempotency and audit/outbox writes. Distributed locks were rejected because the database row is already the consistency boundary.