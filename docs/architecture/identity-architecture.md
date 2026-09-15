# Identity Architecture

Identity owns authentication and authorization state: users, credentials, roles, permissions, refresh tokens, security audit events, and the transactional outbox. It does not own customer profile data.

Registration inserts the user and an `identity.user.registered` outbox message in one PostgreSQL transaction. The outbox publisher later emits the event to `identity.lifecycle.v1` with `UserId` as the partition key so lifecycle events for one identity retain order.

Production mapping: Identity API runs on EKS, PostgreSQL moves to RDS, signing keys come from Secrets Manager/KMS, Kafka maps to MSK, Redis maps to ElastiCache, and traces/logs continue through OpenTelemetry/CloudWatch.
