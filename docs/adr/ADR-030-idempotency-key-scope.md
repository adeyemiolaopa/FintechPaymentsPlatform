# ADR-030 Idempotency Key Scope

## Status

Accepted

## Context

Keys should not be globally unique across all customers forever. Customers should be able to independently use opaque client-generated keys.

## Decision

Payment creation keys are scoped by `CustomerId + OperationType + IdempotencyKey`. For Week 6 the operation type is `CreatePayment`.

## Consequences

Customer A cannot discover Customer B payment resources by guessing the same key. The same key can be reused by different customers without collision. Future operations such as refunds or withdrawals can use the same idempotency infrastructure with separate operation types.

## Alternatives Considered

Global key uniqueness was rejected because it creates unnecessary collisions. Endpoint-only scoping was rejected in favor of operation type because it fits future command APIs that may move paths without changing business semantics.