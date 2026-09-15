# Kafka Topic Catalog

All producer-owned topics are declared in `MessagingTopicCatalog` and bootstrapped by Docker Compose before services start. Topic names are versioned with `.v1`; breaking schema changes require a new topic version.

| Topic | Owner | Events | Partition key | Partitions | Retention |
| --- | --- | --- | --- | --- | --- |
| `identity.lifecycle.v1` | Identity | `identity.user.registered` | user id | 6 | 14 days |
| `customer.lifecycle.v1` | Customer | `customer.lifecycle.changed` | customer id | 6 | 14 days |
| `account.lifecycle.v1` | Account | `account.lifecycle.changed` | account id | 6 | 14 days |
| `account.funds.reservation.v1` | Account | `account.funds.reservation.changed` | account id | 6 | 14 days |
| `account.restriction.v1` | Account | `account.restriction.changed` | account id | 6 | 14 days |
| `beneficiary.lifecycle.v1` | Account | `beneficiary.lifecycle.changed` | customer id | 6 | 14 days |
| `ledger.accounts.v1` | Ledger | `ledger.account.created` | ledger account id | 6 | 14 days |
| `ledger.transactions.v1` | Ledger | `ledger.transaction.posted`, `ledger.transaction.reversed` | ledger transaction id | 6 | 14 days |
| `payments.lifecycle.v1` | Payment | `payment.lifecycle.changed` | payment id | 6 | 14 days |

Local bootstrap uses `docker compose up kafka-topic-bootstrap` or `deploy/kafka/create-topics.ps1`. Production bootstrap must use the same topic names, partition counts, replication factor, and retention policy adjusted for the target cluster.
## Retry and DLQ topics

Local bootstrap also creates retry and DLQ topics explicitly while Kafka auto topic creation remains disabled.

Current active consumer-owned topics:

| Consumer | Retry topic | DLQ topic |
| --- | --- | --- |
| `customer.identity-user-registered-v1` | `identity.lifecycle.v1.retry.1m` | `customer.identity.lifecycle.v1.dlq` |
| `account.customer-lifecycle-v1` | `customer.lifecycle.v1.retry.1m` | `account.customer.lifecycle.v1.dlq` |
| `ledger.account-lifecycle-v1` | `account.lifecycle.v1.retry.1m` | `ledger.account.lifecycle.v1.dlq` |

Bootstrap also reserves retry/DLQ topics for existing producer topics so future consumers do not rely on automatic topic creation.