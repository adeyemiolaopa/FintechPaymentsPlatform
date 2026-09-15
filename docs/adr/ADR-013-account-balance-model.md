# ADR-013 Account Balance Model

## Status
Accepted

## Context
The platform needs a simple Week 3 wallet balance model before full ledger posting exists.

## Decision
Store ledger and reserved balances on Account, derive available balance, and enforce non-negative balance invariants in both domain code and PostgreSQL check constraints.

## Consequences
Reservation flows can be built and tested now without introducing a full accounting ledger prematurely. The model must evolve when ledger posting and settlement are introduced, but the available-balance invariant remains valid.

## Alternatives Considered
A transaction-ledger-only model was deferred because Week 3 needs account and reservation foundations, not full double-entry accounting.