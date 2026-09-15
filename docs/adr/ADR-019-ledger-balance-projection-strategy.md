# ADR-019 Ledger Balance Projection Strategy

## Status
Accepted

## Context
Summing all postings for every current-balance query will not scale forever.

## Decision
Maintain `ledger_account_balances` in the same transaction as postings. Treat it as derived and rebuildable; postings remain authoritative.

## Consequences
Current balance reads are efficient while integrity verification can compare projection to immutable postings.

## Alternatives Considered
A postings-only query model was rejected as the sole strategy for high-volume production reads.