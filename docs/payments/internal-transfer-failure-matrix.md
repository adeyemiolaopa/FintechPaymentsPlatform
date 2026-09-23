# Internal Transfer Failure Matrix

| Failure | Financial outcome | Payment state | Reservation state | Recovery action |
|---|---|---|---|---|
| Duplicate HTTP request | One payment effect | Existing state replayed | One reservation by PaymentId | Return existing payment |
| Account rejects deterministic validation | No ledger effect | Rejected | None or released | No retry |
| Account timeout during reserve | Unknown | PendingValidation | Unknown | Retry reserve with PaymentId |
| Crash after reservation | No finalized ledger yet | PendingValidation or FundsReserved | Active | Retry reserve and continue |
| Ledger timeout after commit | Ledger may be posted | Processing | Active | Retry ledger with PaymentId |
| Ledger rejects before commit | No ledger effect | Failed or recoverable | Released when safe | Release reservation |
| Crash after ledger | Ledger posted once | Processing | Active | Confirm/retry ledger, commit reservation |
| Crash after reservation commit | Ledger posted once | Processing | Committed | Mark Completed |
| Kafka outage | Financial workflow still completes | Completed | Committed | Outbox publishes later |
| Redis outage | No correctness impact | Normal state | Normal state | Continue using PostgreSQL |