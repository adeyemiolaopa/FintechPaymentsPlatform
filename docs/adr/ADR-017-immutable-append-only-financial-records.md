# ADR-017 Immutable Append-only Financial Records

## Status
Accepted

## Context
Financial history must be auditable and corrections must not rewrite prior events.

## Decision
Ledger transactions and postings are append-only. Corrections use new reversal transactions. The DbContext rejects modification or deletion of posted transactions and postings.

## Consequences
Auditability improves and historical balance queries remain reproducible. Operational fixes require compensating records rather than data edits.

## Alternatives Considered
Mutable balances and edited postings were rejected because they obscure financial history.