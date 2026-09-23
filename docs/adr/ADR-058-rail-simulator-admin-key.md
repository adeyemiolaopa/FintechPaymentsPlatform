# ADR-058: Rail Simulator Admin Key

## Status

Accepted

## Decision

Protect simulator admin/configuration endpoints with `X-Rail-Admin-Key`.

## Consequences

Scenario, callback, reset, settlement, and callback-inspection operations remain explicit privileged actions even in local Docker deployments.
