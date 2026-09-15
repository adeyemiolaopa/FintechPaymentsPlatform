# ADR-046: Consumer Lag Monitoring

## Status

Accepted

## Context

Total lag alone hides hot partitions and business delay.

## Decision

Monitor per-partition lag, oldest event age, processing latency, retry rate, DLQ rate, and stalled consumers.

## Consequences

Alerts can distinguish harmless spikes from real business delay.

## Alternatives Considered

Only monitoring total topic lag was rejected.