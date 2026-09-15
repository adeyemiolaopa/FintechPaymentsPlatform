# ADR-011 Money Representation

## Status
Accepted

## Context
Account must represent wallet balances and reservations without floating-point drift or currency ambiguity.

## Decision
Use a domain `Money` value object composed of decimal amount plus ISO-style currency value object. Persist amounts as PostgreSQL `numeric(19,4)` and persist currency as a three-character code.

## Consequences
Arithmetic remains explicit about currency and precision. Four decimal places are enough for current supported payment currencies while leaving room for fractional fees. Currency support is currently guarded in the Account domain and can later move to a central reference-data service.

## Alternatives Considered
Integer minor units were considered, but supported currencies may not all share the same minor-unit scale. Floating-point values were rejected because they are unsafe for money.