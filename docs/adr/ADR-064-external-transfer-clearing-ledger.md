# ADR-064: External Transfer Clearing Ledger Account

Status: Accepted

## Context

Week 11 does not implement settlement reconciliation or destination-bank ledger representation.

## Decision

On definitive provider success, Payment posts a balanced transaction debiting the source wallet liability ledger and crediting a configured external transfer clearing ledger account.

## Consequences

The platform maintains balanced books for outbound external transfers. If the clearing account is missing, Payment remains `PendingReconciliation` and does not commit the reservation.