# ADR-069: Reconciliation service boundary

Accepted. Reconciliation owns evidence, matching and investigation history. Payment, Ledger and Account remain sole owners of their financial state. Reconciliation uses authenticated APIs and controlled Payment recovery; it never directly changes balances or another service's tables. This makes discrepancies visible without inventing financial truth.
