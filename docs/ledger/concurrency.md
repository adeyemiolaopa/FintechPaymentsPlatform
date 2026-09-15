# Ledger Concurrency

Ledger posting relies on PostgreSQL transactions and uniqueness constraints. Idempotency is enforced by a unique external reference/idempotency key plus a canonical SHA-256 request hash.

Balance projection updates lock affected projection rows using `FOR UPDATE` in sorted account-id order. Concurrent postings against overlapping accounts serialize at the projection rows.

The outbox publisher uses `FOR UPDATE SKIP LOCKED` so multiple worker instances can process pending messages without intentionally selecting the same rows. Kafka publication is retried; duplicate publication remains acceptable to consumers.