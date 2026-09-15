# ADR-010 Identity Lifecycle Events via Kafka

## Status
Accepted

## Context
Customer creation depends on successful Identity registration, but Identity must not write directly to Customer storage.

## Decision
Identity writes `IdentityUserRegistered` to an outbox in the same transaction as user creation. The outbox publisher emits to `identity.lifecycle.v1`, partitioned by `UserId`. Customer consumes idempotently.

## Consequences
Registration is not lost during Kafka outages and Customer remains decoupled. Generalized Inbox infrastructure is deferred to Week 8; this workflow uses a focused processed-event table.

## Alternatives Considered
Direct synchronous Customer calls were rejected for coupling and partial failure risk. Publishing Kafka directly inside registration was rejected because it creates a dual-write problem.
