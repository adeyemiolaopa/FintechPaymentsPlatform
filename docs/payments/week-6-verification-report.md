# Week 6 Verification Report

Date: 2026-09-15

## Scope

Week 6 adds request idempotency for `POST /api/v1/payments` using PostgreSQL as the authoritative correctness boundary.

## Architecture Verified

- `Idempotency-Key` is required for payment creation.
- Key scope is `CustomerId + OperationType + IdempotencyKey`.
- Payment requests are canonicalized and hashed with SHA-256.
- The idempotency row and Payment aggregate are created in one Payment database transaction.
- PostgreSQL unique constraint prevents concurrent duplicate ownership.
- Same key plus same request resolves to the same PaymentId.
- Same key plus different request returns an idempotency conflict.
- Duplicate requests while the owner workflow is still processing return `202 Accepted` with the current Payment resource.
- Redis is not used for correctness.
- Kafka outage does not affect request identity; outbox persistence remains local to Payment DB.

## Verification

| Check | Result |
| --- | --- |
| `docker compose -f docker-compose.yml config --services` | Passed; includes `payment-api` once |
| `dotnet format FintechPaymentsPlatform.sln --verify-no-changes --no-restore` | Passed |
| `dotnet build FintechPaymentsPlatform.sln --configuration Release --no-restore` | Passed; 0 warnings, 0 errors |
| `dotnet test tests\Payment\Payments.Payment.UnitTests\Payments.Payment.UnitTests.csproj --configuration Release --no-build --no-restore` | Passed; 17/17 |
| `dotnet test tests\Account\Payments.Account.UnitTests\Payments.Account.UnitTests.csproj --configuration Release --no-build --no-restore` | Passed; 8/8 |
| `dotnet test tests\ArchitectureTests\Payments.ArchitectureTests\Payments.ArchitectureTests.csproj --configuration Release --no-build --no-restore` | Passed; 16/16 |
| `$env:DOCKER_API_VERSION='1.41'; dotnet test tests\Payment\Payments.Payment.IntegrationTests\Payments.Payment.IntegrationTests.csproj --configuration Release --no-build --no-restore` | Passed; 13/13 |
| `docker build -f deploy\docker\Dockerfile.payment -t payments-payment-api:ci .` | Passed |

## Mandatory Scenarios

| Scenario | Evidence |
| --- | --- |
| 100 concurrent duplicate requests | `Concurrent_duplicate_payment_initiation_creates_one_payment_one_reservation_and_one_ledger_effect` passed |
| Same key, changed amount | `Same_key_with_different_payload_returns_conflict_without_second_payment` passed |
| Response lost after commit / service restart replay | `Lost_response_or_service_restart_replay_returns_same_payment_from_postgres` passed |
| Duplicate while original workflow is processing | `Duplicate_request_while_original_workflow_is_processing_returns_accepted_without_second_workflow` passed |
| Redis outage independence | `Request_idempotency_does_not_depend_on_redis_or_kafka_clients` passed; no Redis path is required |
| Kafka outage independence | Same test verifies Payment DB and outbox persistence drive identity, not Kafka availability |
| Downstream idempotency remains separate | Existing Payment tests verify one reservation reference and one ledger external reference per PaymentId |

## Notes

- Payment integration tests require `DOCKER_API_VERSION=1.41` in this local environment because the Docker server reports API version 1.41.
- Multi-instance correctness is represented by concurrent tests creating separate `PaymentService` instances and `PaymentDbContext` instances against the same PostgreSQL database. The correctness boundary is the database unique constraint, not a process-local lock.
- The implementation intentionally returns the current Payment resource on replay, not a frozen original response snapshot.
- Cleanup prunes expired completed response snapshots while preserving key/hash/resource identity.