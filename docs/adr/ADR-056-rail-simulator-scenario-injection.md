# ADR-056: Rail Simulator Scenario Injection

## Status

Accepted

## Decision

Support global scenario configuration and optional per-request scenario override through `X-Rail-Scenario`.

## Consequences

Payment orchestration tests can deterministically exercise success, failure, pending, timeout, throttling, outage, malformed response, duplicate callback, and inconsistent status behavior.
