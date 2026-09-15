# Week 8 Verification Report

Week 8 implements idempotent consumer foundations for the current Kafka consumers in Customer, Account, and Ledger, plus local inbox schema for Payment future consumers.

## Consumers reviewed

- Customer: `IdentityUserRegisteredConsumer` consumes `identity.lifecycle.v1`.
- Account: `CustomerLifecycleConsumer` consumes `customer.lifecycle.v1`.
- Ledger: `AccountLifecycleConsumer` consumes `account.lifecycle.v1` and creates liability ledger accounts.
- Payment: no active hosted Kafka consumer loop currently; `PaymentReferenceHandler` remains a handler-style component and Payment now has the local inbox schema for future consumers.

## Inbox design

Each consuming service owns a local `inbox_messages` table in its service schema. The logical deduplication key is `ConsumerName + EventId`; Kafka offsets are stored for diagnostics only. `PayloadHash` detects the same EventId with a different payload.

Inbox columns include event identity/type/version, consumer name, topic, partition, offset, received/processed timestamps, status, attempt count, last error, payload hash, correlation id, processing instance id, and last updated timestamp.

## Transaction and offset strategy

`KafkaInboxConsumer<TEvent,TDbContext,THandler>` disables auto-commit and commits offsets only after the inbox/business transaction commits, or after a retry/DLQ forwarding decision is safely completed. Handler business logic receives typed event contracts and `IntegrationEventContext`; Kafka types stay in infrastructure.

## Retry, DLQ, and replay

- Immediate retry: bounded retry with jitter.
- Retry topics: one configured retry topic per active consumer.
- DLQ: consumer-owned DLQ topics for active consumers.
- Replay: `deploy/kafka/replay-dlq.ps1` supports dry-run and replay with unchanged envelope payload, preserving EventId, and writes a local audit log.

## Validation performed on 2026-09-15

```powershell
dotnet format FintechPaymentsPlatform.sln --no-restore
dotnet build FintechPaymentsPlatform.sln --no-restore
docker compose config --quiet
dotnet test tests\UnitTests\Payments.UnitTests\Payments.UnitTests.csproj --no-build
dotnet test tests\ArchitectureTests\Payments.ArchitectureTests\Payments.ArchitectureTests.csproj --no-build
$env:DOCKER_API_VERSION='1.41'; dotnet test tests\Customer\Payments.Customer.IntegrationTests\Payments.Customer.IntegrationTests.csproj --no-build
$env:DOCKER_API_VERSION='1.41'; dotnet test tests\Account\Payments.Account.IntegrationTests\Payments.Account.IntegrationTests.csproj --no-build
$env:DOCKER_API_VERSION='1.41'; dotnet test tests\Ledger\Payments.Ledger.IntegrationTests\Payments.Ledger.IntegrationTests.csproj --no-build
$env:DOCKER_API_VERSION='1.41'; dotnet test FintechPaymentsPlatform.sln --no-build
```

Results:

- Build passed with 0 warnings and 0 errors.
- Docker Compose configuration validated successfully.
- Shared unit tests passed: 13/13.
- Architecture tests passed: 20/20.
- Customer integration tests passed: 2/2.
- Account integration tests passed: 3/3.
- Ledger integration tests passed: 9/9.
- Full solution test suite passed, including Payment integration tests: 109/109 total across projects.

## Reliability scenarios covered by automated tests

- Duplicate event handler delivery creates one customer reference/customer record.
- Duplicate account lifecycle delivery creates one ledger liability account.
- Ledger financial idempotency creates one financial effect under concurrent duplicate instructions.
- Inbox message state transitions, stale processing recovery, and payload-hash mismatch detection are covered by unit tests.
- Architecture tests verify live Kafka consumers inherit the inbox consumer framework and domain/API layers do not directly own Kafka client behavior.

## Operational demonstrations documented, not load-tested in this run

The following are documented in runbooks/ADR material but were not executed as live multi-pod Kafka load scenarios in this local run: scaling 1/2/4 consumers, hot-partition generation, rebalance storm simulation, and production DLQ replay approval workflow.

## Intentional deviations

- Payment has inbox schema but no hosted Kafka consumer because the current Payment service does not yet run an active consumer loop.
- Retry topics are implemented as explicit retry streams; delayed delivery semantics are documented but not implemented with a scheduler in this increment.
- Consumer metrics are documented as required metric names; a metrics exporter for per-partition lag is left for the observability increment.

## Week 9 starting point

Add first-class consumer observability: per-group lag collector, oldest event age gauges, DLQ/retry dashboards, stalled-consumer alerts, and an admin-approved replay API with persistent replay audit records.