# ADR-062: Ambiguous External Transfer Outcomes Are Not Retried By Submit

Status: Accepted

## Context

A timeout or server error after provider acceptance may mean money movement already started. Retrying submit can duplicate a payout.

## Decision

After a provider submission is durably recorded, ambiguous outcomes move the payment to `PendingReconciliation`. Recovery only queries provider status by stable references.

## Consequences

Some payments remain pending longer, but duplicate money movement is avoided. Manual investigation uses `RailSubmission`, `RailSubmissionAttempt`, and payment audit events.