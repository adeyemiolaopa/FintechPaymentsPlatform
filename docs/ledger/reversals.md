# Reversals

A reversal is the exact accounting inverse of a posted transaction. It creates a new LedgerTransaction and new postings. It does not mutate the original transaction or original postings.

A unique reversal link enforces one full reversal per original transaction. Duplicate reversal attempts are rejected or resolved idempotently when they use the same reversal reference.

A compensating adjustment is different from a reversal. Adjustments may not be exact inverses and are intentionally left for a future controlled workflow.