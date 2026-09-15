# ADR-028 Payment Request Idempotency

## Status

Accepted

## Context

Payment initiation can be retried by clients, gateways, load balancers, and mobile networks. A retry after response loss must not create another payment workflow or another financial effect.

## Decision

`POST /api/v1/payments` requires `Idempotency-Key`. The Payment service stores an idempotency record and maps one scoped key plus one request hash to one Payment resource.

## Consequences

Retries with the same key and same request return the same PaymentId. Reusing the key with a different request returns `409 Conflict`. Clients must persist keys per logical payment intent.

## Alternatives Considered

Using timestamps or payment attributes was rejected because legitimate payments may share amount, account, beneficiary, and timing. Generic HTTP middleware alone was rejected because Payment needs the resource identity and transaction boundary.