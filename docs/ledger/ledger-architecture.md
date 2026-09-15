# Ledger Architecture

Ledger is the authoritative financial posting service. It owns ledger accounts, posted transactions, postings, reversals, balance projections, idempotency records, audit records, processed integration events, and outbox messages.

Account owns customer wallet lifecycle and operational holds. Ledger owns finalized accounting entries. Account must not query Ledger tables directly, and Ledger must not reference Account infrastructure or database objects.

Ledger consumes `account.lifecycle.v1` and creates customer wallet liability ledger accounts idempotently with `ExternalReference = account:{AccountId}`. Ledger publishes `ledger.accounts.v1` and `ledger.transactions.v1` events through transactional outbox.

Production target: Ledger API on EKS, Ledger PostgreSQL on encrypted RDS Multi-AZ, Kafka on MSK, secrets in AWS Secrets Manager, key management through KMS, and telemetry through OpenTelemetry plus CloudWatch.