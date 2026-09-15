# ADR-004: AWS as Cloud Platform

## Status

Accepted

## Context

The target production platform should run on managed services with strong regional availability options.

## Decision

Prepare abstractions and deployment folders for EKS, RDS PostgreSQL, MSK, ElastiCache, ECR, Secrets Manager, KMS, CloudWatch, S3, Route 53, and ALB.

## Consequences

AWS provides mature managed infrastructure and compliance building blocks. Application code must avoid direct AWS SDK coupling unless isolated behind clear boundaries.

## Alternatives Considered

Azure and GCP can host the same architecture, but AWS is the requested target. Self-managed Kubernetes/data services were rejected for Week 1 operational burden.