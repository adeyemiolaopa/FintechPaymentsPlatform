# ADR-021 Ledger Kafka Partition Strategy

## Status
Accepted

## Context
Ledger events must publish reliably without making Kafka the financial source of truth.

## Decision
Use transactional outbox. Ledger transaction events publish to `ledger.transactions.v1` keyed by transaction id. Ledger account events publish to `ledger.accounts.v1` keyed by ledger account id.

## Consequences
Kafka outages do not roll back committed ledger postings. Pending outbox rows publish when Kafka recovers. Consumers must remain idempotent.

## Alternatives Considered
Publishing directly inside the posting request was rejected because it creates a dual-write failure mode.