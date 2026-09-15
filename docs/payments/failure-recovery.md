# Payment Failure Recovery

Payment is a saga-style orchestrator. It does not use distributed transactions across Payment, Account, and Ledger databases.

Recovery principles:

- Reservation reference is the PaymentId, so retrying reservation is idempotent.
- Ledger external reference is the PaymentId, so retrying ledger posting is idempotent.
- Recoverable states are PendingValidation, FundsReserved, and Processing.
- A background worker batches stale recoverable payments and resumes the workflow.
- If ledger committed but the response timed out, retry returns the existing ledger transaction and Payment can complete once.
- If ledger is unavailable after reservation, Payment remains Processing and recovery retries.

Kafka outage does not roll back local payment or financial effects; outbox messages remain pending.