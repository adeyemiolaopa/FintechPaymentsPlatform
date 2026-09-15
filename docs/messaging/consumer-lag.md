# Consumer Lag

Lag must be measured per consumer group and partition, not only as a single total.

Track:

- lag per partition
- total lag
- oldest unprocessed event age
- consume rate
- processing rate
- handler latency
- retry rate
- DLQ rate

Business age is often more useful than raw lag. For example, "oldest ledger account event is 14 minutes old" is more actionable than "lag is 50,000".

Hot partitions show up as one partition lagging while others drain. Causes include skewed partition keys, a retry storm, slow database work, or downstream dependency latency.