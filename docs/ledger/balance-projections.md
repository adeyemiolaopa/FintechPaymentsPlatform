# Balance Projections

Postings are authoritative. `ledger_account_balances` is a transactionally maintained projection containing debit total, credit total, version, and update time.

Current balance queries use the projection. Historical balance queries derive totals from immutable postings as of a timestamp.

Projection rows are locked in deterministic account-id order during posting to avoid lost updates and reduce deadlock risk. The projection is rebuildable from postings and must never be treated as more authoritative than the postings.