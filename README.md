# FintechPaymentsPlatform

## Overview

`FintechPaymentsPlatform` is the engineering foundation for a production-grade financial transaction platform. Week 1 established the platform foundation. Week 2 added Identity and Customer bounded contexts. Week 3 added Account and Wallet foundations. Week 4 adds a double-entry Ledger service for immutable, balanced, idempotent, auditable financial postings, reversals, balance projections, and integrity verification.

## Current Status

Week 1 - Engineering Foundation - Complete
Week 2 - Identity & Customer - Complete
Week 3 - Accounts & Wallets - Complete
Week 4 - Double-Entry Ledger - Complete
Week 5 - Payment Initiation - Next

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
                    |
                 PostgreSQL
```

Identity owns users, credentials, roles, permissions, refresh tokens, security audit events, and outbox messages. Customer owns customer profile, customer lifecycle status, KYC status foundation, customer audit events, processed integration events, and customer lifecycle outbox messages. Account owns wallet accounts, account numbers, account status, restrictions, operational reservations, beneficiaries, audit events, processed integration events, and account outbox messages. Ledger owns finalized financial postings, ledger accounts, reversals, balance projections, integrity checks, audit events, and ledger outbox messages.

## Technology Stack

C#, .NET 10, ASP.NET Core, PostgreSQL, Redis, Apache Kafka, Schema Registry, Docker Compose, xUnit, FluentAssertions, Testcontainers, Serilog, OpenTelemetry, OpenAPI, and GitHub Actions.

## Local Development

Create local secrets from `.env.example`, or use the generated local `.env` file that is ignored by git.

```bash
docker compose up -d
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
15. `POST /api/v1/auth/refresh`
16. `POST /api/v1/auth/logout`

Privileged customer lifecycle operations require `customer.suspend` or `customer.activate` permissions. Privileged account lifecycle operations require permissions such as `account.freeze`, `account.restrict`, and `account.close`.

## Repository Structure

`src/BuildingBlocks` contains reusable domain, application, infrastructure, messaging, and observability primitives.

`src/Services/Identity` contains the Identity bounded context.

`src/Services/Customer` contains the Customer bounded context.

`src/Services/Account` contains the Account and Wallet bounded context.

`src/Services/Ledger` contains the Double-Entry Ledger bounded context.

`src/Services/Payments.Service.Template` remains as the Week 1 proof service.

`tests` contains unit, integration, and architecture tests.

`deploy` contains Docker, Helm, and Terraform folders.

`docs` contains architecture, ADRs, runbooks, diagrams, and security material.

## Engineering Principles

Domain remains persistence-agnostic. Application orchestrates use cases. Infrastructure owns external concerns. API handles transport only. Endpoints do not call PostgreSQL, Redis, Kafka, or AWS directly. Domain events are internal; integration events are external contracts. Identity, Customer, Account, and Ledger communicate through Kafka contracts, not direct database access.
