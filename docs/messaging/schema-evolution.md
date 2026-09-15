# Schema Evolution

Version 1 event contracts are JSON envelopes with additive compatibility.

Rules:

- Add nullable fields or fields with safe defaults for compatible changes.
- Do not rename, remove, or change the meaning of an existing field in a `.v1` topic.
- Do not change identifier formats or money/currency semantics in place.
- Create a `.v2` topic for breaking changes and dual-publish during migration.
- Keep old consumers working until lag is zero and the old topic retention window has elapsed.

Golden examples live under `docs/messaging/contracts`. Unit tests verify older envelopes without optional routing fields still deserialize.