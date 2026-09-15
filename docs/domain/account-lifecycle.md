# Account Lifecycle

Accounts are opened as Wallet accounts for Week 3 self-service customers. A customer can open an account only after Account has received a Customer lifecycle projection that allows account opening.

Supported account statuses are `Active`, `Frozen`, and `Closed`. Frozen accounts cannot reserve funds. Closed accounts cannot be mutated and cannot be closed while ledger balance, reserved balance, or active reservations remain.

Operational status changes write account audit events and account lifecycle outbox messages. The current implementation allows self-service wallet creation only; additional account types are intentionally reserved for later platform weeks.