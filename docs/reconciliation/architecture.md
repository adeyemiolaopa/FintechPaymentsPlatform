# Reconciliation service architecture

Reconciliation owns settlement imports, observations, runs, matches, exceptions and audit. Payment, Ledger, Account and the rail remain the sole owners of financial state. The service reads them through authenticated internal APIs and can request Payment's idempotent recovery command; it never writes another service's tables or posts ledger entries.

```text
Provider CSV -> file store -> validation -> staged settlement rows -> bounded matching
                                                       |                    |
Payment API <-------------------------------------------+                    +-> exception queue
Ledger API <--------------------------------------------+                    +-> run/audit history
Provider status API (via Payment adapter) -> status reconciliation -> Payment recovery command
```

Provider reference is the preferred key; client reference is fallback. Amount, currency, references, status and ledger posting must agree before a `Matched` result. Missing read-model facts may remain `Pending` during the provider-specific grace window. Contradictions are exceptions; no amount or currency mismatch is auto-resolved. A pending payment with verified provider success or failure may request Payment recovery, which revalidates and performs all financial work idempotently.

Settlement dates are provider business dates, separate from UTC timestamps. The current simulator policy uses a simple delay window; real providers require configured cutoff, market timezone, weekends and holiday rules before reverse matching. A missing row is not proof of provider failure.

Local files are an implementation of `ISettlementFileStore`. The AWS target is a private versioned S3 bucket with SSE-KMS, least-privilege IAM, integrity checking, and policy-driven retention; S3 notification/EventBridge can trigger the same import workflow. The service runs on EKS with RDS PostgreSQL and OpenTelemetry/CloudWatch. Raw files and financial evidence must not be deleted without an approved retention schedule. Never log raw file contents or customer PII.

The active worker scans bounded pending Payment API pages, persists due jobs and exponential backoff in its own database, and claims work with PostgreSQL `SKIP LOCKED` leases. Payment and Ledger lifecycle events populate minimal advisory projections through the Week 8 Inbox base. Existing contracts omit amount and provider references, so authoritative matching still refreshes complete facts through authenticated owning-service APIs. Reconciliation run/exception lifecycle events are written transactionally to an outbox and published to `reconciliation.lifecycle.v1` keyed by run or exception ID.
