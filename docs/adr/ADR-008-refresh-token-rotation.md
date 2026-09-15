# ADR-008 Refresh Token Rotation

## Status
Accepted

## Context
Refresh tokens are long-lived bearer secrets and need replay protection.

## Decision
Store only refresh-token hashes. Rotate refresh tokens on every refresh, revoke old tokens, link replacements, and revoke token families when reuse is detected.

## Consequences
Replay can be detected and contained. Clients must persist the latest refresh token after every refresh.

## Alternatives Considered
Reusable refresh tokens were rejected due replay risk. Server-side access-token blacklists were deferred because short access-token lifetime is sufficient for Week 2.
