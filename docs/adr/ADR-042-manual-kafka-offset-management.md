# ADR-042: Manual Kafka Offset Management

## Status

Accepted

## Context

Committing offsets before database work can lose business processing permanently after a crash.

## Decision

Critical consumers disable auto-commit and commit offsets only after safe database commit, retry-topic publish, or DLQ publish.

## Consequences

Consumers may see duplicates after crashes, and the inbox must handle them. Correctness is preferred over micro-optimized commits.

## Alternatives Considered

Auto-commit was rejected for business consumers.