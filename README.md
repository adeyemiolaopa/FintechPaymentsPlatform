# FintechPaymentsPlatform

## Overview

`FintechPaymentsPlatform` is the engineering foundation for a production-grade financial transaction platform. Week 1 established the platform foundation. Week 2 added Identity and Customer bounded contexts. Week 3 added Account and Wallet foundations. Week 4 added a double-entry Ledger service for immutable, balanced, idempotent, auditable financial postings, reversals, balance projections, and integrity verification. Week 5 added a Payment service for payment intent, explicit lifecycle orchestration, funds reservation, ledger posting, cancellation rules, recovery, audit, and payment lifecycle events. Week 6 added PostgreSQL-backed request idempotency for duplicate-safe payment initiation. Week 7 added Kafka topic governance, standardized transactional outbox delivery, idempotent producer configuration, retry/backoff, failed message inspection, and published-message cleanup. Week 8 added idempotent Kafka consumers, local inbox deduplication, retry/DLQ handling, and replay runbooks. Week 9 hardens internal transfer processing with recovery-safe orchestration, consistency verification, and privileged reversal. Week 10 adds a standalone external payment rail simulator with provider idempotency, scenario injection, delayed processing, callbacks, settlement output, and secured admin controls. Week 11 adds external transfer rail routing, simulator provider integration, durable rail submissions, callback inbox deduplication, ambiguous outcome recovery, and external clearing ledger finalization.

## Current Status

Week 1 - Engineering Foundation - Complete
Week 2 - Identity & Customer - Complete
Week 3 - Accounts & Wallets - Complete
Week 4 - Double-Entry Ledger - Complete
Week 5 - Payment Initiation - Complete
Week 6 - Request Idempotency - Complete
Week 7 - Kafka & Transactional Outbox - Complete
Week 8 - Consumer Inbox & Deduplication - Complete
Week 9 - Internal Transfer Processing - In Progress
Week 10 - External Payment Rail Simulator - In Progress
Week 11 - External Transfers & Rail Routing - In Progress
Week 12 - Reconciliation Engine & Settlement Matching - Complete
Week 13 - Redis, Limits & Risk Controls - Next

## Current Architecture

```text
Client
   |
   +----------> Identity
   |                |
   |             PostgreSQL
   |                |
   |             Outbox
   |                |
   |              Kafka
   |                |
   +----------> Customer
   |                |
   |             PostgreSQL
   |                |
   |             Outbox
   |                |
   |              Kafka
   |                |
   +----------> Account
   |                |
   |             PostgreSQL
   |                |
   |             Outbox
   |                |
   |              Kafka
   |                |
   +----------> Ledger
   |                |
   |             PostgreSQL
   |
   +----------> Payment Rail Simulator
                    |
                 PostgreSQL
```

Identity owns users, credentials, roles, permissions, refresh tokens, security audit events, and outbox messages. Customer owns customer profile, customer lifecycle status, KYC status foundation, customer audit events, processed integration events, and customer lifecycle outbox messages. Account owns wallet accounts, account numbers, account status, restrictions, operational reservations, beneficiaries, audit events, processed integration events, and account outbox messages. Ledger owns finalized financial postings, ledger accounts, reversals, balance projections, integrity checks, audit events, and ledger outbox messages. Payment owns payment intent, request idempotency records, payment state transitions, orchestration metadata, external rail submissions, callback inbox entries, recovery state, audit events, and payment lifecycle outbox messages. The Rail Simulator owns provider-facing transfer simulation, scenarios, callbacks, settlement outputs, and provider idempotency state.

## Technology Stack

C#, .NET 10, ASP.NET Core, PostgreSQL, Redis, Apache Kafka, Schema Registry, Docker Compose, xUnit, FluentAssertions, Testcontainers, Serilog, OpenTelemetry, OpenAPI, and GitHub Actions.

## Local Development

Create local secrets from `.env.example`, or use the generated local `.env` file that is ignored by git.

```bash
docker compose up -d
# Optional: rerun topic creation manually
.\deploy\kafka\create-topics.ps1
dotnet restore
dotnet build
dotnet test
```

Run APIs locally:

```bash
dotnet run --project src/Services/Identity/Payments.Identity.Api
dotnet run --project src/Services/Customer/Payments.Customer.Api
dotnet run --project src/Services/Account/Payments.Account.Api
dotnet run --project src/Services/Ledger/Payments.Ledger.Api
dotnet run --project src/Services/Payment/Payments.Payment.Api
dotnet run --project src/Simulators/Payments.RailSimulator.Api
```

Sample flow:

1. `POST /api/v1/auth/register`
2. Identity writes user plus outbox message transactionally.
3. Outbox publishes `identity.user.registered` to `identity.lifecycle.v1`.
4. Customer consumes the event and creates a customer idempotently.
5. Customer publishes `customer.lifecycle.v1`; Account consumes it into a local customer reference.
6. `POST /api/v1/auth/login`
7. `GET /api/v1/customers/me` with the returned bearer token.
8. `PATCH /api/v1/customers/me` for safe profile changes.
9. `POST /api/v1/accounts` to open a wallet account after customer activation.
10. `GET /api/v1/accounts/{accountId}/balance`
11. `POST /api/v1/accounts/{accountId}/reservations`
12. `POST /api/v1/beneficiaries`
13. Account publishes `account.lifecycle.v1`; Ledger consumes wallet account creation into liability ledger accounts.
14. `POST /api/v1/ledger/transactions` for privileged finalized financial postings.
15. `POST /api/v1/payments` with `Idempotency-Key` to initiate an internal transfer through duplicate-safe Payment orchestration.
16. `GET /api/v1/payments/{paymentId}/timeline` to inspect state history.
17. `POST /api/v1/payments/{paymentId}/verify-consistency` for privileged internal consistency checks.
18. `POST /api/v1/payments/{paymentId}/reverse` for privileged completed-transfer reversal.
19. `POST /api/v1/auth/refresh`
20. `POST /api/v1/auth/logout`

Privileged customer lifecycle operations require `customer.suspend` or `customer.activate` permissions. Privileged account lifecycle operations require permissions such as `account.freeze`, `account.restrict`, and `account.close`.

## Repository Structure

`src/BuildingBlocks` contains reusable domain, application, infrastructure, messaging, and observability primitives.

`src/Services/Identity` contains the Identity bounded context.

`src/Services/Customer` contains the Customer bounded context.

`src/Services/Account` contains the Account and Wallet bounded context.

`src/Services/Ledger` contains the Double-Entry Ledger bounded context.

`src/Services/Payment` contains the Payment Initiation, request idempotency, and state-machine orchestration bounded context.

`src/Services/Payments.Service.Template` remains as the Week 1 proof service.

`src/Simulators/Payments.RailSimulator.*` contains the standalone external payment rail simulator.

`tests` contains unit, integration, and architecture tests.

`deploy` contains Docker, Helm, and Terraform folders.

`docs` contains architecture, ADRs, runbooks, diagrams, and security material.

## Engineering Principles

Payment request idempotency is documented in `docs/payments/idempotency.md`. External transfer routing and operations are documented in `docs/payments/external-transfer-architecture.md` and `docs/runbooks/external-transfer-operations.md`. The payment rail simulator is documented in `docs/rail-simulator`. Internal transfer production flow, recovery, reversal, consistency verification, and concurrency are documented under `docs/payments/internal-transfer-*.md` and `docs/payments/internal-consistency-reconciliation.md`. Kafka topic ownership, outbox delivery, and inbox consumer idempotency are documented in `docs/messaging/topic-catalog.md`, `docs/messaging/event-envelope.md`, `docs/messaging/outbox.md`, and `docs/messaging/inbox-pattern.md`. Domain remains persistence-agnostic. Application orchestrates use cases. Infrastructure owns external concerns. API handles transport only. Endpoints do not call PostgreSQL, Redis, Kafka, or AWS directly. Domain events are internal; integration events are external contracts. Identity, Customer, Account, Ledger, and Payment communicate through Kafka contracts and service-boundary clients, not direct database access.
