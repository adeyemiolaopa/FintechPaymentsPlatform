# ADR-065: Durable Recovery For Rail Payments

Status: Accepted

## Context

External provider calls and callbacks can fail independently from local database commits.

## Decision

Payment recovery includes `SubmittedToRail` and `PendingReconciliation`. It uses `NextStatusCheckAtUtc` and provider status APIs to resolve pending outcomes. Existing submissions are not submitted again.

## Consequences

Recovery is restart-safe and avoids duplicate provider movement. Operational latency is governed by retry/backoff settings and provider status availability.