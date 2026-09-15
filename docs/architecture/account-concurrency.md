# Account Concurrency

Reservation and release operations lock the account row with `SELECT ... FOR UPDATE` inside a database transaction. The lock serializes balance mutations for one account while allowing unrelated accounts to proceed independently.

The reservation flow checks customer eligibility, reservation idempotency, active debit restrictions, and available balance while holding the lock. A unique `(AccountId, ReferenceId)` index makes retrying the same reservation reference idempotent.

`Account.Version` remains a concurrency token for ordinary tracked updates, but balance-changing operations use row locks because overspending prevention must be enforced at the database boundary under concurrent requests.