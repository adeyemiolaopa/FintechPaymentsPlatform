# Rail Simulator Status Model

Transfers move through a provider-style lifecycle:

```text
Received -> Processing -> Successful
                      \-> Failed
```

`Unknown` is only emitted as a query response for inconsistency testing. `Reversed` is reserved for provider reversal simulation and is not currently emitted by the Week 10 transfer flow.

`ReceivedAtUtc`, `ProcessedAtUtc`, and `CompletedAtUtc` provide observability for timing-sensitive client reconciliation tests.
