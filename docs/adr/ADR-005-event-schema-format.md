# ADR-005: Event Schema Format

## Status

Accepted

## Context

Integration events need compatibility guarantees as services evolve.

## Decision

Use Schema Registry locally and prefer Avro for future event contracts. Week 1 publishes JSON envelopes only as a foundation probe; real service events should introduce Avro schemas before production use.

## Consequences

Avro supports compact payloads and compatibility checks. Teams must use additive changes for backward compatibility, version breaking changes deliberately, and never leak domain object serialization directly to Kafka.

## Alternatives Considered

JSON Schema is more human-readable but less compact. Protobuf is excellent for strict contracts but can be less natural for schema-registry-centered Kafka workflows.