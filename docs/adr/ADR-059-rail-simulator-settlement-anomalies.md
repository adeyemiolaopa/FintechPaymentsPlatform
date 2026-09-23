# ADR-059: Rail Simulator Settlement Anomalies

## Status

Accepted

## Decision

Generate settlement records from successful transfers and support anomaly modes for missing rows, duplicate rows, amount mismatches, and unknown references.

## Consequences

Reconciliation tests can cover provider file defects without building one-off fixtures for each failure mode.
