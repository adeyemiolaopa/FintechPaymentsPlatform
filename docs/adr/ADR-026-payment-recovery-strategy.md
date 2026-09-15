# ADR-026 Payment Recovery Strategy

Status: Accepted

A Payment recovery worker scans stale recoverable states in bounded batches and resumes them with stable downstream references. Retried reservation and ledger commands are idempotent by PaymentId.
