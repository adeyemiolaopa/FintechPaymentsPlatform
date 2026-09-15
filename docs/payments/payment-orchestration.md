# Payment Orchestration

Payment Service owns workflow state and coordinates downstream capabilities with local transactions, idempotent commands, and compensation.

It intentionally avoids MSDTC, two-phase commit, and cross-service database transactions. Correctness comes from explicit state transitions, downstream idempotency keys, append-only audit history, and recovery.

Transient downstream failures are not treated as deterministic business rejection. Business failures such as insufficient funds, frozen source account, or currency mismatch become Rejected.