# Partitioning Strategy

Partition keys are business identifiers that preserve ordering where it matters.

- Identity user lifecycle: user id.
- Customer lifecycle: customer id.
- Account lifecycle, reservations, restrictions: account id.
- Beneficiary lifecycle: customer id.
- Ledger account events: ledger account id.
- Ledger transaction events: ledger transaction id.
- Payment lifecycle: payment id.

The outbox writes `PartitionKey` explicitly and the publisher sends it as the Kafka message key. This gives per-aggregate ordering while allowing unrelated aggregates to scale across partitions.