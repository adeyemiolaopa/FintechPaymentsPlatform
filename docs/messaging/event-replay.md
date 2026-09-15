# Event Replay

Replay must preserve the original `EventId`. If the original business effect already committed, the inbox will skip the duplicate. If a corrected event is required, publish a new event and relate it to the original in metadata; do not mutate historical identity.

Replay requirements:

- Privileged approval.
- Dry-run capability.
- Bounded batch size.
- Audit trail with operator, reason, source DLQ, target topic, and EventId.
- Monitoring during and after replay.

Local helper scripts should be treated as operational tooling, not automatic production authorization.