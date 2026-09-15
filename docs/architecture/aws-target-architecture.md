# AWS Target Architecture

No AWS resources are provisioned in Week 1. The target mapping is Kubernetes to Amazon EKS, PostgreSQL to Amazon RDS PostgreSQL, Kafka to Amazon MSK, Redis to Amazon ElastiCache, container registry to Amazon ECR, secrets to AWS Secrets Manager, encryption keys to AWS KMS, logs and metrics to Amazon CloudWatch, object storage to Amazon S3, DNS to Amazon Route 53, and ingress to an Application Load Balancer.

The target network uses public subnets only for load balancers and private subnets for workloads and data services. Production should be Multi-AZ, encrypted in transit and at rest, and governed through least-privilege IAM. Service-to-service authentication should use a workload identity and mTLS or signed service tokens. Application code should consume secrets through abstractions so AWS Secrets Manager can replace local configuration without rewriting use cases.
## MSK Consumer Operations

Kafka consumers run on EKS against private Amazon MSK brokers. Each business consumer uses a versioned group id and least-privilege IAM/ACL permissions for only the topics it consumes, retry topics it reads, and DLQs it writes or replays.

Autoscaling should consider consumer lag, oldest event age, processing rate, and CPU. CPU alone is not enough for Kafka workloads. Partition count bounds useful active consumers per group; scaling above partition count only adds standby capacity.

Consumer telemetry should flow through OpenTelemetry and CloudWatch-compatible metrics: per-partition lag, oldest event age, retry rate, DLQ count, processing duration, and stalled consumer detection. Multi-AZ connectivity must be validated so rolling deployments and broker failover do not cause sustained rebalance storms.