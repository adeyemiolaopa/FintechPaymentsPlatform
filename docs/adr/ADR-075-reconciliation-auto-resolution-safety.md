# ADR-075: Reconciliation auto-resolution safety policy

Accepted. Only an internally pending Payment with verified final provider status and matching independent references, amount and currency can request idempotent Payment recovery. Reconciliation never directly posts ledger or finalizes reservations. Amount/currency mismatches, failed Payment/provider success, completed Payment/provider failure and ledger/provider contradictions require review; no automatic reversal occurs.
