# ADR-037: Transactional Outbox Standard

## Status

Accepted

## Context

Each bounded context had similar outbox tables and publishers. Week 7 standardizes producer reliability.

## Decision

Every service outbox stores event identity, type/version, aggregate metadata, topic/key/partition key, JSON payload, headers, status, attempts, retry timestamps, and last error.

## Consequences

Operators can inspect and repair delivery state consistently across schemas. Migrations add required metadata to existing outbox tables.