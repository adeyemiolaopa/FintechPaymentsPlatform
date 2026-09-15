# ADR-014 Funds Reservation Model

## Status
Accepted

## Context
Payment authorization requires temporary holds that can survive retries and Kafka outages.

## Decision
Represent holds as `FundsReservation` records with active/released lifecycle state and make `(AccountId, ReferenceId)` unique. Reservation lifecycle messages are published through the Account outbox.

## Consequences
Payment workflows can retry safely with the same reference. Active reservations reduce available balance until released or later extended to committed/expired handling.

## Alternatives Considered
Storing only a reserved-balance number was rejected because it loses idempotency, traceability, and lifecycle history.