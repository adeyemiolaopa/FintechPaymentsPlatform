# Consumer Groups

Each logical business consumer uses its own consumer group. Unrelated consumers must not share a group because Kafka distributes partitions within a group.

Example for `payments.lifecycle.v1` with 12 partitions:

| Consumer | Group | Max active instances |
| --- | --- | --- |
| Notification | `notification-service-v1` | 12 |
| Fraud | `fraud-service-v1` | 12 |
| Analytics | `analytics-service-v1` | 12 |

Current platform groups:

- Customer consuming Identity: `customer-service-v1`.
- Account consuming Customer: `account-service-v1`.
- Ledger consuming Account: `ledger-service-v1`.

More instances than partitions can join a group, but excess instances stay idle. Different groups receive the full stream independently.