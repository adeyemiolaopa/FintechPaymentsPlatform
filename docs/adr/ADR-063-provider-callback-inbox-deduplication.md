# ADR-063: Provider Callback Inbox Deduplication

Status: Accepted

## Context

Providers can deliver callbacks more than once or out of order.

## Decision

Payment stores callbacks in `rail_callback_inbox` with a unique `(Provider, CallbackEventId)` key before applying state changes. Duplicate callbacks return success without repeating ledger or reservation effects.

## Consequences

Callback processing is idempotent. Conflicting callbacks are audited and require operational review instead of automatic state regression.