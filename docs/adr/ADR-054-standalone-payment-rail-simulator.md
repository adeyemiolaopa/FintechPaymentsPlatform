# ADR-054: Standalone Payment Rail Simulator

## Status

Accepted

## Decision

Build the external payment rail simulator as a standalone bounded context under `src/Simulators`, with its own API, application contracts, domain model, infrastructure, database schema, and tests.

## Consequences

Core payment services can test provider behavior without coupling to a real provider or sharing simulator data with production bounded contexts.
