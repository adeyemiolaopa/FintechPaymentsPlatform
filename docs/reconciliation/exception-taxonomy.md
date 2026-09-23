# Exception taxonomy

Structured codes include missing Payment, Provider, Ledger and Settlement records; amount, currency, status and reference mismatch; duplicate provider or internal records; unknown provider reference; Payment completed/provider failed; Payment failed/provider success; ledger posted/provider failed; provider success/ledger missing; and unmatched settlement record.

- **Low:** optional nonfinancial metadata missing.
- **Medium:** settlement record missing after the configured delay; can reflect provider timing.
- **High:** provider success with internal failure, duplicate provider row, unknown external reference.
- **Critical:** amount/currency mismatch, ledger/provider contradiction, completed Payment/provider failure.

Exception state is Open, Investigating, Resolved or Ignored. Assignment never changes financial truth. New evidence should be linked to the existing active exception and append an audit event; it should not create repeated active tickets.
