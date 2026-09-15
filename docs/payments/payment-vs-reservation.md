# Payment vs Reservation

Reservation is an operational hold owned by Account Service. It is not a financial debit.

Payment reserves funds before processing. After ledger posting succeeds, Payment commits the reservation to remove the hold. If ledger posting fails definitively before financial commit, Payment releases the reservation and moves to Failed.

Week 5 corrected Account reservation commit semantics so commit reduces only ReservedBalance and does not mutate LedgerBalance.