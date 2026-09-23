# Internal Transfer Production Flow

Internal wallet-to-wallet transfer is a Payment-owned saga. Payment owns intent, state, audit, recovery, idempotency, and lifecycle events. Account owns account eligibility and source funds reservations. Ledger owns finalized double-entry postings and reversals.

Flow:

1. Client sends `POST /api/v1/payments` with `Idempotency-Key`.
2. Payment creates one payment intent and stores the request hash.
3. Payment validates customer, source account ownership, destination account existence, currency, and account status.
4. Payment reserves source funds through Account using `PaymentId` as the stable reservation reference.
5. Payment posts a balanced ledger transaction through Ledger using `PaymentId` as the stable external reference.
6. Payment commits the source reservation after ledger confirmation.
7. Payment marks the payment `Completed`, records audit/state transitions, and writes an outbox event.

The response may be replayed from request idempotency storage. Client timeouts never define the financial outcome.