# Internal Consistency Reconciliation

Payment exposes read-only consistency verification for internal transfers.

Checks include:

- Completed or reversed payment has an original ledger transaction id.
- Completed or reversed payment has a committed source reservation.
- Reservation reference equals `PaymentId`.
- Reservation amount and currency match the payment.
- Ledger external reference equals `PaymentId`.
- Ledger debit total equals credit total.
- Ledger amount and currency match the payment.
- Reversed payment has a reversal ledger transaction id.

The checker reports violations. It does not mutate financial state or silently repair mismatches.