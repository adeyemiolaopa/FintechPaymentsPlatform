# ADR-002: PostgreSQL as Primary Database

## Status

Accepted

## Context

Financial workflows need durable relational consistency and rich transactional support.

## Decision

Use PostgreSQL through EF Core and keep domain models persistence-agnostic. Future production deployment maps to Amazon RDS PostgreSQL.

## Consequences

PostgreSQL gives mature transactions, indexes, and operational familiarity. Care is required around UTC time, concurrency, migrations, and avoiding shared databases between services.

## Alternatives Considered

DynamoDB offers scale and AWS-native operations but complicates relational invariants. SQL Server is viable but less aligned with the requested stack.