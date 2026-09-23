# ADR-055: Rail Simulator Provider Idempotency

## Status

Accepted

## Decision

Use `(ClientId, ClientReference)` as the provider idempotency key and compare a canonical financial-instruction hash for conflict detection.

## Consequences

Retries return the same provider reference, while changed instructions with reused references return a provider-style conflict.
