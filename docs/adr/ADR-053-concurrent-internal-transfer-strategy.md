# ADR-053 Concurrent Internal Transfer Strategy

Internal-transfer concurrency is controlled with PostgreSQL row locks, unique idempotency keys, deterministic ledger projection locking, and bounded recovery. Redis is not part of financial correctness.
