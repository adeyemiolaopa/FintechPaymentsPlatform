# ADR-060: Rail Simulator Local Schema Creation

## Status

Accepted

## Decision

Use EF Core `EnsureCreatedAsync` for the simulator in `Development` and `Testing` while the service is still a local/test simulator.

## Consequences

The simulator starts quickly in tests and Docker Compose. Before non-local deployment, this should be replaced by migrations to provide controlled schema evolution.
