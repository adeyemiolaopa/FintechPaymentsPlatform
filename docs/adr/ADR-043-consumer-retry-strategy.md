# ADR-043: Consumer Retry Strategy

## Status

Accepted

## Context

Retrying forever on a partition blocks later messages and can create retry storms.

## Decision

Use bounded immediate retries with jitter, then a deliberate retry topic, then DLQ for poison or exhausted messages.

## Consequences

Retry topics can reorder events. Consumers that need sequencing must validate state/version.

## Alternatives Considered

Infinite in-process retry was rejected.