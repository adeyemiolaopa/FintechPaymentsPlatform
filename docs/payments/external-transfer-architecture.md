# External Transfer Architecture

Week 11 adds the first production-facing external transfer foundation for `ExternalBankTransfer` payments. The implementation routes outbound transfers through a rail router and provider adapter boundary instead of embedding provider behavior in the payment workflow.

## Components

- `IRailRouter` selects an enabled provider by market, currency, payment type, and priority.
- `IPaymentRailAdapter` owns provider protocol details: submit, status query, destination validation, authentication, timeout classification, and throttling classification.
- `SimulatorRailAdapter` integrates the Week 10 rail simulator with HMAC request signing.
- `RailSubmission` stores one durable provider submission per payment and enforces stable `(Provider, ClientReference)` idempotency.
- `RailSubmissionAttempt` records every outbound submit attempt and its classified result.
- `RailCallbackInbox` records provider callbacks by `(Provider, CallbackEventId)` so duplicates are safe.

## State Machine

External transfer flow:

1. `Initiated`
2. `PendingValidation`
3. `FundsReserved`
4. `Processing`
5. `SubmittedToRail`
6. `Completed`, `Failed`, or `PendingReconciliation`

`PendingReconciliation` means the platform must not resend money movement. Recovery only asks the provider for status by stable client/provider reference. Definitive success finalizes once. Definitive failure releases the reservation and fails the payment. Ambiguous or pending outcomes keep the reservation active.

## Provider Idempotency

The client reference is the payment id formatted as `D`. The payment service stores a hash of the immutable transfer instruction plus provider. If recovery sees the same payment with a different instruction hash, it parks the payment in `PendingReconciliation` for manual investigation.

## Outcome Classification

| Condition | Classification | Action |
| --- | --- | --- |
| HTTP 2xx with successful provider status | Definitive success | Post external clearing ledger transaction, commit reservation, complete payment |
| HTTP 2xx with failed provider status or business failure | Definitive failure | Release reservation, fail payment |
| Provider timeout after submission uncertainty | Ambiguous | Keep reservation, schedule status recovery |
| Provider 5xx after submission uncertainty | Ambiguous | Keep reservation, schedule status recovery |
| Provider throttling | Pending | Keep reservation, respect retry/backoff |
| Network unavailable before definitive acceptance | Pending | Keep reservation, retry status later |
| Malformed provider response | Ambiguous | Keep reservation, require status recovery/manual investigation |
| Invalid callback signature | Rejected callback | No payment state change |
| Duplicate callback event | Duplicate callback | No additional financial effect |
| Conflicting terminal callback after completion | Conflict audit | Do not regress completed payment |

## Ledger Semantics

External transfers do not credit a customer destination ledger account in Week 11. On provider success, Payment posts a balanced ledger transaction that debits the source wallet liability ledger account and credits the configured external transfer clearing ledger account. If `ExternalTransfers:DefaultClearingLedgerAccountId` is empty, provider success remains `PendingReconciliation` instead of committing a reservation without a ledger posting.

## Callbacks

The callback endpoint is `POST /api/v1/rails/callbacks/{provider}`. It is not bearer-authenticated; it validates provider HMAC over `timestamp.rawBody` using `X-Rail-Timestamp` and `X-Rail-Signature`. Accepted callbacks are durably inserted into `rail_callback_inbox` before payment state changes.

## Recovery

The payment recovery worker includes `SubmittedToRail` and `PendingReconciliation`. Recovery checks `RailSubmission.NextStatusCheckAtUtc` and calls provider status APIs. It never repeats a provider submit for an existing submission unless no submission record exists.

## Local Configuration

`ExternalTransfers` config selects `SimulatorRail` for `NG`/`NGN` external bank transfers. Docker Compose wires payment-api to `rail-simulator-api`. To enable automatic simulator callbacks locally, configure the simulator callback URL after startup:

```bash
curl -X PUT http://localhost:5110/api/v1/config/callback \
  -H "X-Rail-Admin-Key: local-rail-admin-key" \
  -H "Content-Type: application/json" \
  -d '{"url":"http://payment-api:8080/api/v1/rails/callbacks/SimulatorRail","webhookSecret":"local-webhook-secret"}'
```

For host-to-container testing, use the externally reachable payment URL instead of `payment-api`.

## Observability

Payment audit events record rail preparation, submit attempt start, provider result classification, status checks, callback processing, reservation finalization, and ledger finalization. Rail submission tables provide the provider reference, raw status, retry timing, and callback inbox status needed for support investigations.

Week 11 intentionally does not implement settlement reconciliation. Settlement files and anomalies remain simulator-side foundations for Week 12.