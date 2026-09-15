# ADR-001: Service Architecture

## Status

Accepted

## Context

The platform will support high-volume money movement and must avoid accidental coupling between bounded contexts.

## Decision

Use Clean Architecture inside each service and explicit contracts between services. The template service proves the shape without creating real payment domains.

## Consequences

The architecture is testable and keeps infrastructure replaceable. It requires more discipline and architecture tests than a simple CRUD service.

## Alternatives Considered

A single layered monolith was simpler but would make future service extraction risky. A fully distributed design on day one was rejected because it would front-load operational complexity.