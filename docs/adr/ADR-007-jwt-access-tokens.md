# ADR-007 JWT Access Tokens

## Status
Accepted

## Context
APIs need authenticated access without a shared session database on every request.

## Decision
Issue short-lived JWT access tokens containing only identity, role, permission, and customer id claims. Signing keys are configured externally and production will use Secrets Manager/KMS.

## Consequences
Services can validate tokens locally. Stolen tokens remain valid until expiration, so lifetimes are kept short.

## Alternatives Considered
Opaque reference tokens were deferred because centralized introspection is not yet needed. Long-lived JWTs were rejected because revocation is difficult.
