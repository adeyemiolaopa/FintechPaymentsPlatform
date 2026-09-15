# Integrity Verification

The integrity verifier checks that transactions are balanced, posting currencies match transaction currency, projection totals match postings, and reversal links point to existing transactions.

The endpoint `POST /api/v1/ledger/integrity/verify` requires `ledger.integrity.read`. It records audit success as `BalanceProjectionVerified` and failures as `IntegrityCheckFailed`.

Production verification should run as a scheduled administrative job with batching and database aggregation. It must report mismatches; it must not silently overwrite production balances.