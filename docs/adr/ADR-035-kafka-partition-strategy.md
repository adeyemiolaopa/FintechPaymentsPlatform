# ADR-035: Kafka Partition Strategy

## Status

Accepted

## Context

Consumers need per-aggregate ordering without serializing unrelated traffic through one partition.

## Decision

Use business aggregate identifiers as Kafka message keys and outbox `PartitionKey` values. Payment events use payment id, account events use account id, customer events use customer id, and ledger transaction events use ledger transaction id.

## Consequences

Ordering is guaranteed per key. Cross-aggregate ordering is not guaranteed and workflows must not depend on it.