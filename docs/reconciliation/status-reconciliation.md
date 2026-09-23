# Active status reconciliation

For `SubmittedToRail`, `PendingReconciliation` and `Processing` payments, query the provider by provider reference, falling back to client reference. The Payment Service's adapter handles provider authentication, retry and circuit breaking. Persist each status observation. If provider is still pending/unknown or unavailable, leave Payment state unchanged and check later with backoff.

Only complete independent amount, currency and client-reference evidence can justify a Payment recovery request. Payment revalidates the latest rail state and performs ledger/reservation work idempotently. Reconciliation never posts ledger or commits/releases a reservation itself. A failed provider result can likewise request Payment recovery only when the ledger is absent and the internal Payment is pending. A provider-success/failed-Payment or provider-failed/completed-Payment conflict requires human investigation.

```text
Pending Payment -> status query -> evidence match -> Payment recovery command -> fresh Payment/Ledger read -> Resolved or Exception
```

The worker discovers pending rail payments through a bounded internal Payment API query, persists jobs with next-check time, attempt count and last-check time, claims due work using `FOR UPDATE SKIP LOCKED`, and schedules exponential backoff. An explicit status-run endpoint is also available for operators. Provider API outage does not fail a Payment.
