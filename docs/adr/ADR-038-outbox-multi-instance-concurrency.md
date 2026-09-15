# ADR-038: Outbox Multi-Instance Concurrency

## Status

Accepted

## Context

Services may run multiple instances, so publishers must not publish the same pending row concurrently.

## Decision

Publishers claim pending rows inside a PostgreSQL transaction using `FOR UPDATE SKIP LOCKED`, then mark successes and failures before commit.

## Consequences

Multiple workers can drain the same outbox table safely. Long broker calls hold row locks for the selected batch only, so batch size must remain bounded.