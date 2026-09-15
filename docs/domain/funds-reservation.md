# Funds Reservation

Funds reservation places a temporary hold on available balance for a payment workflow. A reservation has an account, reference id, amount, currency, status, creation time, expiry time, and release or commit timestamps.

The reservation API is idempotent by `(AccountId, ReferenceId)`. Retrying the same reference returns the existing reservation instead of creating a second hold.

A reservation can be created only when the account is active, the customer reference allows reservations, the account has no active debit-blocking restriction, the currency matches, and available balance is sufficient. Release removes the hold and emits a reservation lifecycle event.