# ADR-074: Settlement-file idempotency

Accepted. `(provider, SHA-256(raw bytes))` identifies a logical file; filename alone does not. Unique file/line identity and committed checkpoints prevent row duplication after restart. A changed file under the same name is a new import. Duplicate provider references within one file are exceptions, not double-counted matches.
