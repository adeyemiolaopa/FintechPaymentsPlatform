# Balance Model

Account tracks `LedgerBalance`, `ReservedBalance`, and derived `AvailableBalance`.

`LedgerBalance` is the durable balance snapshot owned by Account. Week 3 does not implement deposits, settlement, or ledger posting; test-only seeding exists only for persistence and concurrency verification.

`ReservedBalance` is the sum of active holds created by funds reservations. `AvailableBalance` is `LedgerBalance - ReservedBalance` and is never persisted independently.

Money is represented as decimal `numeric(19,4)` in PostgreSQL. Domain value objects reject negative amounts, over-precision beyond four decimal places, and cross-currency arithmetic.

After Week 4, Ledger postings are the authoritative source of truth for finalized financial movement. Account balance fields are operational/read-model state for wallet workflows and reservations; they must not compete with Ledger as the accounting record. Reservations remain holds until a future payment workflow commits finalized ledger postings.
