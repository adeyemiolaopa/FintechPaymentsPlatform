# Dead-Letter Topics

DLQ topics are consumer-owned where failure semantics differ.

Current consumer DLQs:

- `customer.identity.lifecycle.v1.dlq`
- `account.customer.lifecycle.v1.dlq`
- `ledger.account.lifecycle.v1.dlq`

DLQ messages preserve the original payload and add headers such as original topic, partition, offset, failure category, retry count, and failure reason. A DLQ is not a solution without monitoring, ownership, and replay. `DLQ count > 0` requires operational review.