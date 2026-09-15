# ADR-022 Payment Service as Workflow Orchestrator

Status: Accepted

Payment Service owns payment workflow state and orchestration, while Account owns reservations and Ledger owns financial postings. This preserves service ownership boundaries and gives operations a durable payment timeline.
