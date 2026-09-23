# Internal Transfer Recovery

Recovery scans `PendingValidation`, `FundsReserved`, `Processing`, and `ReversalPending` payments older than the configured threshold using bounded batches and PostgreSQL `FOR UPDATE SKIP LOCKED`.

Decision matrix:

| Payment state | Reservation | Ledger | Action |
|---|---|---|---|
| PendingValidation | None/unknown | None | Validate and reserve using PaymentId |
| FundsReserved | Active | None | Move to Processing |
| Processing | Active | None/unknown | Retry ledger post using PaymentId |
| Processing | Active | Posted | Commit reservation |
| Processing | Committed | Posted | Mark Completed |
| ReversalPending | Original posted | Reversal unknown | Retry reversal using `REV-{PaymentId}` |

Timeout is treated as unknown, not failure. Recovery uses stable references instead of creating new financial instructions.