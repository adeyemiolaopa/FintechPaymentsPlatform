# ADR-016 Double-entry Ledger As Source Of Truth

## Status
Accepted

## Context
Week 3 Account balances are operational account state. Finalized money movement needs accounting invariants that cannot be spread across services.

## Decision
Introduce Ledger as the authoritative source of truth for finalized financial postings. Account remains responsible for customer wallet lifecycle and operational reservations.

## Consequences
Financial reporting and settlement will build on Ledger. Account balances must evolve toward read models/projections sourced from Ledger for finalized movement.

## Alternatives Considered
Keeping finalized balances in Account was rejected because it mixes lifecycle/holds with immutable accounting history.