# ADR-009 Role and Permission Authorization

## Status
Accepted

## Context
Roles are useful for broad assignment but too coarse for fintech operations.

## Decision
Seed broad roles and fine-grained permissions. APIs declare permission policies, while application services enforce ownership and domain authorization.

## Consequences
Authorization rules remain testable and do not devolve into scattered role checks. Claims can grow if permissions become numerous, so permission references may be introduced later.

## Alternatives Considered
Hard-coded Admin checks were rejected. Pure RBAC was rejected because operational permissions need finer granularity.
