# ADR-039: Event Schema Evolution

## Status

Accepted

## Context

Integration events are external contracts between bounded contexts.

## Decision

Within `.v1` topics, schema changes must be additive and backward compatible. Breaking changes require `.v2` topics, dual publishing, and consumer migration.

## Consequences

Event contracts move slower than internal models. Golden examples and tests protect envelope compatibility.