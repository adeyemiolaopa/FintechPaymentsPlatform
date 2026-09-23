# Internal Transfer Reversal

Week 9 introduces privileged reversal for completed internal transfers.

Endpoint: `POST /api/v1/payments/{paymentId}/reverse`

Rules:

- Requires `payment.reverse`.
- Original payment must be `Completed` or already `ReversalPending`/`Reversed`.
- Original ledger transaction must exist.
- Reversal uses stable external reference `REV-{PaymentId}`.
- Destination account must have sufficient available funds under the conservative Week 9 policy.
- Original ledger transaction remains immutable.
- Ledger creates inverse postings and records the reversal relationship.
- Payment appends `ReversalPending` and `Reversed` state transitions.

Duplicate reversal requests return the existing reversed state and do not create a second ledger effect.