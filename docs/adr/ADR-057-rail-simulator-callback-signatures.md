# ADR-057: Rail Simulator Callback Signatures

## Status

Accepted

## Decision

Sign callbacks with HMAC-SHA256 over `timestamp.rawBody` and include the signature in `X-Rail-Signature`.

## Consequences

Consumers can test webhook authenticity, replay windows, and tamper detection with stable local credentials.
