# ADR-015 Account Service Customer Reference Strategy

## Status
Accepted

## Context
Account must enforce customer eligibility rules, but direct Customer database reads would couple bounded contexts.

## Decision
Customer publishes lifecycle events to `customer.lifecycle.v1`. Account consumes them idempotently into a local customer reference projection containing customer id, status, KYC status, and updated time.

## Consequences
Account can make local eligibility decisions while Customer remains the source of truth for customer lifecycle. Account creation may be temporarily unavailable immediately after registration until the Customer event is consumed.

## Alternatives Considered
Synchronous Customer API calls were rejected for availability coupling. Shared Customer tables were rejected because they break bounded context ownership.