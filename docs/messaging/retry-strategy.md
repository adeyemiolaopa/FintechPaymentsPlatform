# Consumer Retry Strategy

Consumer failures are classified as transient, deterministic business, poison/contract, or infrastructure failures.

Current implementation:

- Immediate retry: bounded in-process retry with jitter.
- Retry topic: first extended retry topic per consumer, preserving `EventId` and original metadata.
- DLQ: poison, schema, contract, integrity, or retry-topic failures go to a dead-letter topic.

Retry topics trade ordering for availability. If `E1` moves to retry and `E2` succeeds on the main topic, `E2` can be observed first. Consumers that require strict aggregate sequencing must validate aggregate state/version explicitly.