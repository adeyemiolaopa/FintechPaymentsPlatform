# Week 12 Verification Report

Date: 2026-09-23

## Implementation

1. **Architecture:** A four-project Reconciliation service separates API, application contracts, domain evidence/state, and PostgreSQL/Kafka/file infrastructure.
2. **Service boundaries:** Reconciliation owns runs, files, matches, exceptions, observations, audit, Inbox projections, and its outbox. Payment and Ledger remain the only owners of financial state.
3. **Active status reconciliation:** A bounded scheduled worker leases durable jobs with `FOR UPDATE SKIP LOCKED`, queries provider status by provider/client reference, and applies exponential backoff.
4. **Settlement ingestion:** The authorized CSV endpoint streams uploads through `ISettlementFileStore`, validates the complete file, stages bounded batches, and rejects malformed input conservatively.
5. **File identity:** SHA-256 over raw bytes plus provider is unique; filename is metadata only. Identical bytes return the existing logical import.
6. **Large files:** Parsing is streaming, staging and matching batches are configurable, and no complete file is loaded into memory.
7. **Crash/restart:** Durable staged records, `LastProcessedLine`, per-record uniqueness, and expiring leases allow replay after process loss without duplicate effects.
8. **Matching hierarchy:** Provider reference is primary; client/payment reference is fallback. Amount, destination, and time are never primary identity.
9. **Provider references:** Provider and client references are validated, indexed, compared, and preserved as evidence without customer-profile duplication.
10. **Ledger matching:** Ledger is queried through its authenticated reconciliation API, by transaction ID first and immutable external Payment reference second. Reconciliation never writes postings.
11. **Reverse matching:** Completed provider-success payments expected in the business-date window are compared back to file records to detect `SETTLEMENT_RECORD_MISSING`.
12. **Exception taxonomy:** Structured codes cover missing, duplicate, reference, amount, currency, status, provider/payment, and provider/ledger conflicts.
13. **Severity:** Low, Medium, High, and Critical classifications are deterministic and documented.
14. **Evidence hierarchy:** Provider policy determines final statuses, settlement authority, NotFound behavior, grace, and settlement delay. Evidence remains append-only.
15. **Auto-resolution:** Only matching references/amount/currency with a pending owner state can call the idempotent Payment recovery command. Conflicts never trigger a direct correction or reversal.
16. **Manual resolution:** Authorized assignment, resolve, ignore, provider requery, payment recovery, and ledger verification commands require actor, reason, comment, and controlled downstream semantics.
17. **Audit:** Immutable events record file receipt/rejection/completion, run start, matches, exception creation/assignment/resolution, and corrective-command requests.
18. **Events:** A transactional outbox publishes run-completed and exception-created/resolved lifecycle facts to `reconciliation.lifecycle.v1`, keyed by aggregate ID and carrying stable event IDs.
19. **Read models:** Payment and Ledger lifecycle consumers use the Week 8 Inbox base and maintain minimal local projections; complete matching facts are refreshed through service APIs.
20. **Eventual consistency:** Provider-specific missing-reference grace prevents premature exceptions while events are in flight; amount/currency contradictions receive no grace once both records exist.
21. **Metrics:** Run, record, match, exception, resolution, file, duration, open-age, open-count, and active-backlog metrics are emitted through the Reconciliation meter.
22. **Alerts:** The control monitor checks critical exceptions, match-rate threshold, exception age, backlog, provider-success/ledger-missing, and explicitly configured provider file schedules.
23. **Runbooks:** Provider-success/ledger-missing, unknown reference, amount mismatch, missing file, and exception-spike procedures are present.

## Verification Results

24. **Integration tests:** 14/14 Reconciliation integration tests pass against PostgreSQL with the production Npgsql retry strategy enabled.
25. **Large-file results:** 10,000 perfect rows matched with 0 exceptions in 9.29 seconds and 31 MiB managed heap on this workstation.
26. **Duplicate-file results:** Same provider/hash returns one import; same filename with different content creates a distinct import.
27. **Mismatch results:** Duplicate, unknown reference, amount, currency, failed-payment/provider-success, completed-payment/provider-failed, and missing settlement cases classify without financial mutation.
28. **Crash recovery:** A simulated process failure after three lookups resumes after lease expiry and completes 10/10 records with one match and settlement record per input row.
29. **Performance baseline:** 100,000 perfect rows matched with 0 exceptions in 59.36 seconds and 123 MiB managed heap. This is a local baseline, not a production capacity claim; the optional 1,000,000-row stress run was not performed.
30. **Docker:** Reconciliation and Rail Simulator images are in Compose; PostgreSQL schema, Kafka topics, dependencies, health checks, ports, and durable settlement storage are configured. The full stack is healthy and Reconciliation readiness returns `Healthy`.
31. **CI:** Restore, strict Week 12 formatting, Release build/test, Reconciliation and simulator image builds, Compose validation, and dependency scanning are configured. Local Release build completed with 0 warnings/errors.
32. **Documentation:** Architecture, active status, files, matching, taxonomy, manual resolution, evidence policy, alerts, sequence diagrams, retention, S3/KMS target, and segregation of duties are documented.
33. **ADRs:** ADR-069 through ADR-075 cover service boundary, settlement model, matching, exception workflow, evidence, file idempotency, and safe auto-resolution.
34. **Problems found:** Engineering review found a deployed outbox transaction outside Npgsql's retry execution strategy and test configuration that did not mirror production retry behavior.
35. **Fixes applied:** Outbox, settlement-file lease, and active-job lease transactions now run through `CreateExecutionStrategy`; integration tests now enable the same retry strategy. Rebuilt container logs show repeated worker cycles without the former exception.
36. **Intentional deviations:** Full maker-checker approval and S3/EventBridge delivery remain prepared/documented future work as allowed by scope. Current lifecycle events do not contain every matching fact, so Inbox projections are advisory and authenticated service APIs supply complete evidence. The 1,000,000-row run remains optional and was not claimed.
37. **Week 13 starting point:** Add Redis-backed velocity limits and risk controls at Payment authorization, preserving ledger/reconciliation boundaries and using stable decision/audit contracts.

## Evidence Summary

- `dotnet restore FintechPaymentsPlatform.sln`: passed.
- `dotnet build FintechPaymentsPlatform.sln --configuration Release`: passed, 0 warnings and 0 errors.
- Strict Week 12 `dotnet format --verify-no-changes`: passed.
- Reconciliation unit tests: 12/12 passed.
- Reconciliation integration tests: 14/14 passed.
- Architecture tests: 24/24 passed.
- Rail Simulator integration tests: 6/6 passed.
- Full solution test run: passed across every project when run sequentially; the sequential mode avoids saturating the local Docker API with concurrent Testcontainers startup.
- `docker compose config --quiet`: passed.
- `docker compose up -d --build`: passed.
- `docker compose ps`: all long-running services healthy; topic bootstrap exited 0.
- `GET http://localhost:5107/health/ready`: `Healthy`.

The implementation distinguishes exact matches, temporary/event-lag pending evidence, deterministic owner-mediated recovery, and unsafe financial conflicts requiring review. Reading every row is not treated as reconciliation success.