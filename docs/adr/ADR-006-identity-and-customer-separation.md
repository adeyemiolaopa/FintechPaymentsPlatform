# ADR-006 Identity and Customer Separation

## Status
Accepted

## Context
Identity answers authentication and authorization questions. Customer answers financial-services customer profile and lifecycle questions. Combining them would blur ownership and make future compliance controls harder.

## Decision
Create separate Identity and Customer bounded contexts with separate APIs, DbContexts, database names, and schemas. Customer consumes Identity lifecycle events through shared integration contracts only.

## Consequences
This prevents direct cross-service data access and keeps ownership clear. It introduces eventual consistency between registration and customer creation.

## Alternatives Considered
A single user/customer module was rejected because it encourages a distributed monolith. Shared EF relationships were rejected because they break service ownership.
