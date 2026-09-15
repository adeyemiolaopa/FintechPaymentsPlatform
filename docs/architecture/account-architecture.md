# Account Architecture

Account owns wallet accounts, account numbers, account status, restrictions, balance snapshots, funds reservations, beneficiaries, audit events, processed integration events, and account outbox messages.

Account does not read Customer tables. Customer lifecycle state is projected locally from `customer.lifecycle.v1` into `account.customer_references`. Account creation and reservations require an active customer reference; duplicate customer lifecycle messages are ignored through `processed_integration_events`.

The API exposes self-service account creation, account listing, balance lookup, funds reservation and release, beneficiary management, and operational account controls. Self-service operations derive `CustomerId` from JWT claims. Operational actions require explicit permissions such as `account.freeze`, `account.restrict`, and `account.close`.

Persistence is PostgreSQL in the `account` schema. Money values are stored as `numeric(19,4)`, with database check constraints enforcing non-negative ledger and reserved balances and non-negative available balance. Integration events are published through an outbox to avoid dual writes.

Ledger Service owns finalized double-entry postings. Account Service owns customer account lifecycle and operational restrictions/reservations. Account-created events are consumed by Ledger to create wallet liability ledger accounts; Account does not write Ledger tables directly.
