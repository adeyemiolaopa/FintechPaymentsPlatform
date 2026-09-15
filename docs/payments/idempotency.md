# Payment Request Idempotency

Week 6 adds public API idempotency for `POST /api/v1/payments`.

## Contract

`POST /api/v1/payments` requires:

```http
Idempotency-Key: <client-generated-key>
```

The client must generate the key once before the first submission of a logical payment intent, persist it locally, and reuse the same key for every retry. A client must not generate a new key after a timeout if the logical payment intent is the same.

Accepted keys are opaque strings between 8 and 128 characters using letters, digits, `.`, `_`, `:`, or `-`. UUIDs are recommended but not required.

## Scope

Keys are scoped by:

```text
CustomerId + OperationType + IdempotencyKey
```

For Week 6 the operation type is `CreatePayment`. Two different customers can use the same opaque key without seeing or colliding with each other.

## Persistent Record

Payment stores idempotency records in PostgreSQL table `payment.payment_idempotency_records`.

Important fields:

```text
CustomerId
OperationType
IdempotencyKey
RequestHash
Status
ResourceType
ResourceId
ResponseStatusCode
ResponseBody
ProcessingStartedAtUtc
ExpiresAtUtc
CompletedAtUtc
```

The mandatory correctness constraint is:

```text
UNIQUE(CustomerId, OperationType, IdempotencyKey)
```

The first request that inserts this row owns payment creation. Concurrent duplicates race against the database constraint, not process memory or Redis.

## Request Hash

The Payment application canonicalizes the financially meaningful request and hashes it with SHA-256.

Included fields:

```text
SourceAccountId
Payment Type
Amount
Currency
Destination
Description
```

Excluded fields:

```text
CorrelationId
TraceId
Timestamp
Authorization
Idempotency-Key itself
Transport headers unrelated to payment intent
```

Normalization:

```text
payment type -> enum name
currency -> uppercase
amount -> invariant decimal form up to 4 decimal places
bank code -> uppercase trimmed
country code -> uppercase trimmed
account/name/description -> trimmed, case preserved
```

`Rent` and `rent` intentionally produce different hashes because arbitrary free text is not case-normalized.

## Behavior

Same key and same request:

```text
return the existing PaymentId and current payment representation
never create a second Payment
never reserve funds twice
never post ledger twice
```

Same key and different request:

```http
409 Conflict
```

Problem type:

```text
https://errors.example.com/idempotency-conflict
```

Duplicate while original request is still processing:

```http
202 Accepted
Location: /api/v1/payments/{paymentId}
Idempotency-Replayed: true
```

The response contains the current payment representation. It may show `Initiated`, `PendingValidation`, `FundsReserved`, or `Processing` depending on where the original workflow is when the retry arrives.

Completed replay:

```http
201 Created
Location: /api/v1/payments/{paymentId}
Idempotency-Replayed: true
```

For payments, retries return the current resource state while preserving the original resource identity. A retry may therefore observe `Completed` even if the first HTTP response was lost while the workflow was earlier in progress.

## Transaction Boundary

The initial claim uses one Payment database transaction:

```text
BEGIN
  INSERT IdempotencyRecord
  INSERT Payment
  INSERT PaymentStateTransition
  INSERT PaymentAuditEvent
  INSERT OutboxMessage
  UPDATE IdempotencyRecord.ResourceId
COMMIT
```

This avoids the orphan case where a key is committed without a Payment. If the HTTP response is lost after commit, a retry resolves the existing row and returns the same PaymentId.

## Layered Idempotency

Request idempotency does not replace downstream idempotency.

```text
Layer 1: Client -> Payment API
  Idempotency-Key scoped by customer and operation

Layer 2: Payment -> Account Service
  PaymentId is used as the reservation reference

Layer 3: Payment -> Ledger Service
  PaymentId is used as the ledger external reference

Layer 4: Kafka consumers
  EventId/inbox idempotency arrives in the Kafka week
```

Each boundary protects a different retry surface.

## Retention And Cleanup

Initial retention is configurable with `PaymentIdempotency:RetentionDays` and defaults to 7 days.

Completed response snapshots may be pruned after expiry, but the key/hash/resource link is retained. This favors long-lived duplicate protection over aggressive key reuse. Cleanup batches only completed records with expired response bodies and uses PostgreSQL batching with `FOR UPDATE SKIP LOCKED`; it never deletes active `Processing` rows.

## Redis And Kafka

Redis is not authoritative for request idempotency. The implementation does not require Redis for correctness. Redis may later cache completed lookups, but PostgreSQL remains the source of truth.

Kafka availability does not affect request identity. If Kafka is unavailable, payment creation persists and the outbox retains unpublished lifecycle events. Retrying the same key still resolves the same payment.

## Failure Matrix

| Scenario | Expected behavior | Financial consequence | Recovery path |
| --- | --- | --- | --- |
| Duplicate client retry | Existing PaymentId returned | No duplicate reservation or ledger posting | PostgreSQL idempotency record replay |
| 100 concurrent duplicates | One idempotency row and one Payment | One reservation workflow and one ledger effect | Unique constraint selects owner; others replay |
| Same key, changed amount | `409 Conflict` | No second payment | Client must use a new key for a new intent |
| Response lost after commit | Retry returns same PaymentId | No duplicate workflow | Idempotency row points to resource |
| Payment service crash after Payment commit | Retry returns same PaymentId, possibly `202` | No duplicate workflow | Payment recovery worker continues state machine |
| Redis outage | Request path still works | No duplicate workflow | PostgreSQL remains authoritative |
| Kafka outage | Payment persists, outbox pending | No duplicate workflow | Outbox publisher retries later |
| Payment database unavailable before claim | Request fails safely | No downstream reservation or ledger effect | Retry after database recovery |

## Sequences

First request:

```text
Client -> Payment API: POST /payments + Idempotency-Key X
Payment API -> PostgreSQL: claim X and create Payment in one transaction
Payment API -> Account: reserve with PaymentId reference
Payment API -> Ledger: post with PaymentId external reference
Payment API -> PostgreSQL: complete idempotency record
Payment API -> Client: 201 + PaymentId
```

Retry:

```text
Client -> Payment API: POST /payments + same X + same payload
Payment API -> PostgreSQL: unique key exists
Payment API -> PostgreSQL: hash matches and resource exists
Payment API -> Client: existing PaymentId/current state
```

Conflict:

```text
Client -> Payment API: POST /payments + same X + different payload
Payment API -> PostgreSQL: unique key exists
Payment API -> PostgreSQL: hash differs
Payment API -> Client: 409 idempotency conflict
```