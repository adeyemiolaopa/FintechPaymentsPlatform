# ADR-034: Kafka Topic Design

## Status

Accepted

## Context

Identity, Customer, Account, Ledger, and Payment publish integration events that must remain independently deployable and operationally inspectable.

## Decision

Use service-owned, versioned Kafka topics with names ending in `.v1`. The authoritative catalog is `MessagingTopicCatalog` and `docs/messaging/topic-catalog.md`. Docker Compose bootstraps topics explicitly and disables broker auto creation.

## Consequences

Topic drift becomes visible during local development and CI. Breaking event changes require a new topic version and a migration period.