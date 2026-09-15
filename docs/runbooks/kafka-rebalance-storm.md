# Runbook: Kafka Rebalance Storm

Symptoms: frequent partition revocation/assignment, stalled processing, duplicate redelivery, or lag spikes during deployments.

Checks:

- Pod restarts or rolling deployments.
- `max.poll.interval.ms` too low for handler duration.
- Long synchronous downstream calls.
- Network interruptions between EKS pods and MSK brokers.
- Consumer count greater than partition count.

Actions:

1. Stabilize crashing pods.
2. Reduce handler time or move long delays to retry topics.
3. Tune `MaxPollIntervalMs` deliberately.
4. Avoid scaling beyond partition count for throughput expectations.
5. Verify inbox duplicate skips after the rebalance.