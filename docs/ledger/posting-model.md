# Posting Model

A LedgerTransaction contains immutable postings. Each posting has an account, side, positive amount, currency, sequence, optional description, and creation time.

Debits and credits are represented as `Side + positive Amount`. Negative amounts are rejected. Multi-leg transactions are supported as long as total debit equals total credit.

Posting is atomic: validate idempotency, validate accounts, validate account currencies/statuses, validate balance, insert transaction, insert postings, update projection, write audit, write outbox, then commit.

If PostgreSQL fails before commit, no partial ledger state is accepted. If the client times out after commit, retrying the same external reference returns the existing transaction when the request hash matches.