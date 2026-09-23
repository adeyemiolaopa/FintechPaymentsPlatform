# Provider success, ledger missing

1. Confirm provider reference, client reference, amount, currency and final status from a fresh provider query and settlement evidence. Preserve observation IDs and times.
2. Check current Payment status and its rail submission. Check Ledger transaction by Payment external reference as well as recorded transaction ID; a missing pointer does not prove no posting.
3. Check reservation state. Do not manually post ledger or commit the reservation.
4. If Payment remains pending and evidence agrees, invoke Payment's idempotent recovery command with an operation identifier. Requery Payment, Ledger and reservation. Confirm exactly one posting and a completed Payment, then rerun reconciliation.
5. If Payment is failed/completed, amount differs, or ledger evidence conflicts, keep a high/critical exception open and escalate for maker-checker review.
