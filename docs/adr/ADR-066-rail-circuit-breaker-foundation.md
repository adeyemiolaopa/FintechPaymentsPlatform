# ADR-066: Rail Circuit Breaker Foundation

Status: Accepted

## Context

Provider throttling or outages can cause repeated failures and increase ambiguous states.

## Decision

The simulator adapter records provider failures in an in-memory circuit breaker using configured threshold and open interval. Open circuits classify new attempts as pending instead of hammering the provider.

## Consequences

The current breaker is per-process and foundational. Production deployments should back this with distributed provider health and richer fallback policy before multi-provider failover is enabled.