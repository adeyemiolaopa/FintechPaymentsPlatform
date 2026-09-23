# ADR-061: Rail Router And Provider Adapter Boundary

Status: Accepted

## Context

External transfers need provider-specific authentication, routing, timeout behavior, and status lookup without coupling Payment orchestration to one provider.

## Decision

Payment uses `IRailRouter` to choose a provider route and `IPaymentRailAdapter` to submit and query transfers. The first adapter is `SimulatorRailAdapter` for the Week 10 simulator.

## Consequences

Provider integrations are replaceable. Payment workflow stores durable provider submission state independent of provider protocol details. Fallback policy can be added at the router without changing payment state semantics.