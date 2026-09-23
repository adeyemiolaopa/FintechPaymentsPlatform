# Rail Simulator Scenarios

Scenario mode can be configured globally through `PUT /api/v1/admin/scenario` or overridden per transfer with `X-Rail-Scenario` when the client is allowed to override scenarios.

## Supported Modes

- `Success`: transfer is accepted and completed successfully.
- `Failure`: transfer is accepted and completed as failed.
- `PendingThenSuccess`: transfer remains `Processing` until a worker tick completes it.
- `PendingThenFailure`: transfer remains `Processing` until a worker tick fails it.
- `TimeoutBeforeProcessing`: client receives `504`; no transfer is persisted.
- `TimeoutAfterProcessing`: client receives `504`; transfer is persisted and completed.
- `Http500BeforeProcessing`: client receives `500`; no transfer is persisted.
- `Http500AfterProcessing`: client receives `500`; transfer is persisted and completed.
- `Throttled`: client receives `429`.
- `Outage`: client receives `503`.
- `DuplicateCallback`: terminal transfer schedules multiple callbacks.
- `NoCallback`: terminal transfer schedules no callbacks.
- `MalformedResponse`: API returns malformed JSON for client parser testing.
- `InconsistentStatus`: first status query reports `Processing`, second reports `Unknown`, later queries report the real status.

## Failure Matrix

| Scenario | HTTP Result | Persisted | Final Status | Callback |
| --- | --- | --- | --- | --- |
| `Success` | `200` | Yes | `Successful` | Yes |
| `Failure` | `200` | Yes | `Failed` | Yes |
| `PendingThenSuccess` | `200` | Yes | `Processing` then `Successful` | After terminal |
| `PendingThenFailure` | `200` | Yes | `Processing` then `Failed` | After terminal |
| `TimeoutBeforeProcessing` | `504` | No | n/a | No |
| `TimeoutAfterProcessing` | `504` | Yes | `Successful` | Yes |
| `Http500BeforeProcessing` | `500` | No | n/a | No |
| `Http500AfterProcessing` | `500` | Yes | `Successful` | Yes |
| `Throttled` | `429` | No | n/a | No |
| `Outage` | `503` | No | n/a | No |
