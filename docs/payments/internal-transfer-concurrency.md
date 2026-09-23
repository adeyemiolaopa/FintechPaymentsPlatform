# Internal Transfer Concurrency

Correctness relies on PostgreSQL, not Redis locks.

Account reservations lock the affected account row and atomically check current status, restrictions, currency, and available balance before increasing reserved balance. Concurrent overspend attempts contend on the source account row, so total active reservations cannot exceed spendable funds.

Ledger postings lock balance projections in deterministic ledger-account id order. One `PaymentId` maps to one ledger idempotency record and one financial instruction hash. The same key with different postings is rejected.

Unrelated account pairs can progress independently. Hot accounts naturally serialize at the reservation row and are documented as a future scaling concern rather than hidden behind global locks.