# ADR-048 Internal Transfer Orchestration

Payment Service orchestrates internal transfers as a saga. Account owns reservations and Ledger owns final postings. This avoids distributed transactions while preserving service boundaries through stable idempotency references.
