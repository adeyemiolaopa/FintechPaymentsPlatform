# ADR-031 Request Canonicalization And Hashing

## Status

Accepted

## Context

The service must distinguish a retry of the same logical request from accidental reuse of the same key with a different payment intent.

## Decision

Payment creation requests are canonicalized and hashed with SHA-256. The hash includes source account, payment type, amount, currency, destination, and description. It excludes correlation ids, trace ids, timestamps, authorization, and unrelated transport metadata.

## Consequences

JSON property order and transport metadata do not change request identity. Currency, payment type, bank code, and country code are normalized. Description is trimmed but remains case-sensitive, so `Rent` and `rent` are different intents.

## Alternatives Considered

Storing the full request body was rejected to reduce sensitive data retention. Hashing raw JSON was rejected because property order and insignificant formatting would make retries unstable.