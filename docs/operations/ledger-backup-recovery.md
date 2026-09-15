# Ledger Backup And Recovery

Ledger data is high-value financial data and requires stricter controls than reference data.

AWS target controls: encrypted RDS PostgreSQL Multi-AZ, automated backups, point-in-time recovery, encrypted snapshots with KMS, restricted administrative access, audited restore operations, and Secrets Manager for credentials.

Indicative targets: RPO should be near-zero within the primary region using RDS durability and PITR. RTO depends on business continuity requirements and must be agreed with stakeholders before production launch.

Backups are not sufficient unless restore is tested. Restore tests should validate schema, migrations, ledger invariants, balance projection rebuild, and outbox recovery behavior.