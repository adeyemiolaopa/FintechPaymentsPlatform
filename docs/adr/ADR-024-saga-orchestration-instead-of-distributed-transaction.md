# ADR-024 Saga Orchestration Instead of Distributed Transaction

Status: Accepted

Payment workflows cross Payment, Account, and Ledger services. The platform will not use MSDTC or two-phase commit. It uses local transactions, idempotent downstream commands, recovery, and compensation.
