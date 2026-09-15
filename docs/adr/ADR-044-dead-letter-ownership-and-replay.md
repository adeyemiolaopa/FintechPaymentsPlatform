# ADR-044: Dead-Letter Ownership and Replay

## Status

Accepted

## Context

A DLQ without ownership and replay becomes hidden data loss.

## Decision

Use consumer-owned DLQs for active consumers and require replay to preserve EventId with audit records and approval.

## Consequences

Operational ownership is explicit. Replay tooling must be guarded before production use.

## Alternatives Considered

A single shared DLQ was rejected because consumer failure semantics differ.