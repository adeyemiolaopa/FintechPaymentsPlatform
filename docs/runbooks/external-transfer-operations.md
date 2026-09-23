# External Transfer Operations Runbook

## Provider Timeout Or 5xx After Submission

Impact: the provider may have accepted the transfer, so duplicate submission can move money twice.

Actions:

1. Inspect `payment.rail_submissions` by `PaymentId` or `ClientReference`.
2. Confirm payment status is `SubmittedToRail` or `PendingReconciliation`.
3. Do not create a new payment or manually resubmit to the provider.
4. Let the payment recovery worker query provider status after `NextStatusCheckAtUtc`.
5. If status remains ambiguous beyond the provider SLA, escalate with `ClientReference`, `ProviderReference`, `InstructionHash`, and audit events.

## Provider Circuit Open

Impact: new provider submits are deferred as pending while the circuit is open.

Actions:

1. Check recent `RailSubmissionAttempt` rows for repeated `CIRCUIT_OPEN`, `429`, `NETWORK`, or `Http5xx` raw statuses.
2. Confirm provider health with the simulator/provider status endpoint.
3. Leave existing payments recoverable; do not bypass idempotency.
4. Re-enable traffic by waiting for the configured open interval or deploying a provider/fallback policy change.

## Invalid Provider Callback

Impact: callback is rejected and no payment state changes.

Actions:

1. Confirm headers `X-Rail-Timestamp` and `X-Rail-Signature` are present.
2. Confirm timestamp is within the allowed replay window.
3. Confirm provider webhook secret matches `ExternalTransfers:Providers:*:WebhookSecret`.
4. Check `rail_callback_inbox` for rejected status and audit reason.
5. Ask provider to resend after correcting signature configuration.

## Duplicate Callback

Impact: duplicate callback is acknowledged without another ledger post or reservation commit.

Actions:

1. Query `rail_callback_inbox` by `(Provider, CallbackEventId)`.
2. Verify only the first callback is processed.
3. Verify payment has at most one ledger transaction id and one reservation commit.
4. Treat further duplicate callbacks as provider noise unless payload conflicts with the processed terminal status.

## Provider Status Conflict

Impact: provider sends a terminal status that conflicts with a completed payment or stored provider reference.

Actions:

1. Do not regress a completed payment automatically.
2. Compare callback amount, currency, client reference, provider reference, and raw status with `rail_submissions`.
3. Export payment timeline, callback inbox row, and rail submission attempts.
4. Open an operations investigation before any manual adjustment.

## Clearing Ledger Missing

Impact: provider success is known, but the platform cannot safely finalize ledger and reservation state.

Actions:

1. Configure `ExternalTransfers:DefaultClearingLedgerAccountId` to a valid clearing ledger account.
2. Confirm the ledger account is active in Ledger service.
3. Run payment recovery after configuration is fixed.
4. Verify one external clearing ledger transaction and one reservation commit.