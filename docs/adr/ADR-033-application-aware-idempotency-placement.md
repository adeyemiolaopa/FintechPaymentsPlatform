# ADR-033 Application-Aware Idempotency Placement

## Status

Accepted

## Context

Payment idempotency needs authenticated customer identity, canonicalized business request data, PaymentId resource identity, audit entries, outbox entries, and one safe transaction boundary.

## Decision

Idempotency is implemented in the Payment application/infrastructure service path, not as generic ASP.NET middleware. The API extracts the `Idempotency-Key`; the Payment service validates, hashes, claims, creates the Payment, and completes the idempotency record.

## Consequences

The initial idempotency claim and Payment creation share one transaction. The domain aggregate remains free of HTTP header concepts. The implementation can later be reused for refunds, withdrawals, funding, and adjustments without premature generic abstraction.

## Alternatives Considered

Pure middleware was rejected because it cannot safely coordinate Payment creation and response semantics. A broad generic framework was rejected until more operation types exist.