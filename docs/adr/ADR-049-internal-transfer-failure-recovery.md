# ADR-049 Internal Transfer Failure Recovery

Recoverable transfer states are retried by a bounded worker. Ambiguous downstream failures remain recoverable and are resolved by retrying idempotent Account and Ledger operations with the original PaymentId.
