# Inbox Pattern

Kafka delivery is at least once, so consumers must be idempotent. Each consuming service owns a local `inbox_messages` table in its own schema.

Processing order:

1. Poll Kafka with auto-commit disabled.
2. Deserialize and validate the event envelope.
3. Claim `ConsumerName + EventId` in the local inbox.
4. Execute business logic in the same database transaction where practical.
5. Mark the inbox row `Processed`.
6. Commit the database transaction.
7. Commit the Kafka offset.

If the process crashes after the database commit but before offset commit, Kafka redelivers and the inbox skips the duplicate. If the process crashes before database commit, PostgreSQL rolls back the inbox claim and business changes, so redelivery can process normally.