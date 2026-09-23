# Payment Rail Simulator Architecture

The payment rail simulator is a standalone bounded context under `src/Simulators`. It models an external bank/payment provider without sharing databases with the core services.

## Projects

- `Payments.RailSimulator.Domain`: provider-side entities, status model, scenarios, request hashing, provider references.
- `Payments.RailSimulator.Application`: request/response DTOs and simulator service/authentication contracts.
- `Payments.RailSimulator.Infrastructure`: EF Core persistence, scenario execution, provider idempotency, HMAC request validation, callbacks, settlement generation, and workers.
- `Payments.RailSimulator.Api`: minimal HTTP API, health checks, OpenAPI, and secured admin endpoints.

## Runtime

The simulator stores state in PostgreSQL schema `rail` inside database `payments_rail_simulator`. Startup in `Development` and `Testing` calls `EnsureCreatedAsync` and seeds:

- default provider scenario
- default callback configuration
- default provider client credentials

Transfer submission is provider-idempotent by `(ClientId, ClientReference)`. The simulator returns the existing provider reference for exact duplicate financial instructions and returns `409` for the same client reference with different financial fields.

## Background Work

`RailDelayedProcessingWorker` completes due pending transfers. `RailCallbackWorker` posts signed webhook callbacks and schedules bounded retries.
