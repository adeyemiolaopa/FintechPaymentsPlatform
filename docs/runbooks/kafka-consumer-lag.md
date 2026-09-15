# Runbook: Kafka Consumer Lag

Symptoms: increasing group lag, old events, or downstream state falling behind.

Checks:

1. Inspect lag per group and partition.
2. Compare producer rate with consumer processing rate.
3. Check oldest event age from envelope `OccurredAtUtc`.
4. Inspect service logs for retries, DLQ writes, database latency, or downstream timeouts.
5. Check for one hot partition caused by a skewed key.

Recovery:

- Scale consumers only up to the partition count.
- Increase partitions only with a partitioning review.
- Fix database/downstream bottlenecks before adding more consumers.
- Pause deploys if rebalance storms are increasing lag.